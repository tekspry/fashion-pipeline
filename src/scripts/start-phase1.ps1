$ErrorActionPreference = "Stop"

$root = Join-Path $PSScriptRoot ".." | Resolve-Path

# ── Cleanup: Kill any lingering FashionPipeline processes from previous runs ──
$staleProcs = Get-Process | Where-Object { $_.ProcessName -like "FashionPipeline*" }
if ($staleProcs) {
    Write-Host "Killing $($staleProcs.Count) lingering FashionPipeline processes..."
    $staleProcs | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$services = @(
    @{ Path = "McpServers\FashionPipeline.VisionMcpServer"; Exe = "FashionPipeline.VisionMcpServer.exe"; Url = "http://localhost:5100" },
    @{ Path = "McpServers\FashionPipeline.PromptMcpServer"; Exe = "FashionPipeline.PromptMcpServer.exe"; Url = "http://localhost:5200" },
    @{ Path = "McpServers\FashionPipeline.ImageMcpServer"; Exe = "FashionPipeline.ImageMcpServer.exe"; Url = "http://localhost:5300" },
    @{ Path = "McpServers\FashionPipeline.VideoMcpServer"; Exe = "FashionPipeline.VideoMcpServer.exe"; Url = "http://localhost:5400" },
    @{ Path = "Agents\FashionPipeline.OrchestratorAgent"; Exe = "FashionPipeline.OrchestratorAgent.exe"; Url = "http://localhost:5050" },
    @{ Path = "Agents\FashionPipeline.VisionAgent"; Exe = "FashionPipeline.VisionAgent.exe"; Url = "http://localhost:5101" },
    @{ Path = "Agents\FashionPipeline.CreativeAgent"; Exe = "FashionPipeline.CreativeAgent.exe"; Url = "http://localhost:5201" },
    @{ Path = "Agents\FashionPipeline.ImageAgent"; Exe = "FashionPipeline.ImageAgent.exe"; Url = "http://localhost:5301" },
    @{ Path = "Agents\FashionPipeline.VideoAgent"; Exe = "FashionPipeline.VideoAgent.exe"; Url = "http://localhost:5401" },
    @{ Path = "FashionPipeline.Api"; Exe = "FashionPipeline.Api.exe"; Url = "http://localhost:5000" }
)

foreach ($s in $services) {
    $projectDir = Join-Path $root $s.Path
    $exePath = Join-Path $projectDir "bin\Debug\net8.0\$($s.Exe)"
    Write-Host "Starting $($s.Exe) on $($s.Url)..."

    # UseShellExecute = true → opens each service in its own console window
    # Pass URL and environment via command-line args (supported by ASP.NET Core)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = $projectDir
    $psi.UseShellExecute = $true
    $psi.Arguments = "--urls $($s.Url) --environment Development"
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host ""
Write-Host "Phase 1 stack starting."
Write-Host "  API Swagger : http://localhost:5000/swagger"
Write-Host "  Hangfire    : http://localhost:5000/hangfire"
Write-Host "  Orchestrator: http://localhost:5050/.well-known/agent-card.json"
Write-Host ""
Write-Host "Send header X-Tenant-Id: <guid> on API requests."