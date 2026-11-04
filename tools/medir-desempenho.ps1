<#
.SYNOPSIS
  Mede CPU, GPU e memória do Jubuddy num cenário e devolve uma linha de resultado.

.EXAMPLE
  # Buddy andando, no papel de parede, por 30 s (depois de 8 s de aquecimento):
  .\tools\medir-desempenho.ps1 -Cenario "andando" -AppArgs "--forcar-atividade=andar"

  # Pausado (como se houvesse um jogo em tela cheia):
  .\tools\medir-desempenho.ps1 -Cenario "pausado" -AppArgs "--forcar-pausa"

.NOTES
  - CPU: tempo de processador do processo / tempo de relógio / núcleos (igual ao Gerenciador de Tarefas).
  - GPU: soma da utilização dos motores 3D do processo (contadores "GPU Engine" do Windows, lidos crus via WMI,
    então funciona em Windows em qualquer idioma).
  - Memória: "conjunto de trabalho privado" (a coluna Memória do Gerenciador de Tarefas), além do working set total.
  - Usa uma pasta de dados separada por padrão, para não mexer no seu quarto.
#>
param(
    [string]$Cenario = "teste",
    [string]$Exe = (Join-Path $PSScriptRoot "..\build\Jubuddy.exe"),
    [string[]]$AppArgs = @(),
    [string]$Dados = (Join-Path $env:TEMP "jubuddy-medicao"),
    [int]$Aquecimento = 8,
    [int]$Segundos = 30,
    [string]$Csv = ""
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Dados | Out-Null

function Get-GpuRaw([int]$procId) {
    $rows = Get-CimInstance -ClassName Win32_PerfRawData_GPUPerformanceCounters_GPUEngine -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "pid_$($procId)_*engtype_3D" }
    $sum = [double]0
    $ts = [double]0
    foreach ($r in $rows) { $sum += [double]$r.UtilizationPercentage; $ts = [double]$r.Timestamp_Sys100NS }
    return @{ Sum = $sum; Ts = $ts }
}

function Get-PrivateWs([int]$procId) {
    $p = Get-CimInstance -ClassName Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$procId" -ErrorAction SilentlyContinue
    if ($p) { return [math]::Round([double]$p.WorkingSetPrivate / 1MB, 1) }
    return $null
}

$allArgs = @("--", "--dados=$Dados") + $AppArgs
$proc = Start-Process -FilePath $Exe -ArgumentList $allArgs -PassThru
try {
    Start-Sleep -Seconds $Aquecimento
    $proc.Refresh()
    $cores = [Environment]::ProcessorCount
    $cpu0 = $proc.TotalProcessorTime
    $t0 = Get-Date
    $g0 = Get-GpuRaw $proc.Id

    $memSamples = @()
    for ($i = 0; $i -lt $Segundos; $i++) {
        Start-Sleep -Seconds 1
        $memSamples += Get-PrivateWs $proc.Id
    }

    $proc.Refresh()
    $cpu1 = $proc.TotalProcessorTime
    $t1 = Get-Date
    $g1 = Get-GpuRaw $proc.Id

    $elapsed = ($t1 - $t0).TotalSeconds
    $cpuPct = ($cpu1 - $cpu0).TotalSeconds / $elapsed / $cores * 100
    $gpuPct = if ($g1.Ts -gt $g0.Ts) { ($g1.Sum - $g0.Sum) / ($g1.Ts - $g0.Ts) * 100 } else { 0 }
    $priv = ($memSamples | Where-Object { $_ -ne $null } | Measure-Object -Average -Maximum)

    $result = [pscustomobject]@{
        Cenario        = $Cenario
        Argumentos     = ($AppArgs -join " ")
        Segundos       = [math]::Round($elapsed)
        CPU_pct        = [math]::Round($cpuPct, 2)
        GPU_pct        = [math]::Round($gpuPct, 2)
        MemPrivada_MB  = [math]::Round($priv.Average, 0)
        MemPrivadaMax  = [math]::Round($priv.Maximum, 0)
        WorkingSet_MB  = [math]::Round($proc.WorkingSet64 / 1MB, 0)
        Commit_MB      = [math]::Round($proc.PrivateMemorySize64 / 1MB, 0)
    }
    if ($Csv) {
        $exists = Test-Path $Csv
        $result | Export-Csv -Path $Csv -Append -NoTypeInformation -Delimiter ";" -Encoding UTF8
    }
    $result
}
finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
