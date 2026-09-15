"""Repository-root security gates. Downloads only a pinned checksum-verified scanner to ignored artifacts."""
import hashlib, io, json, os, pathlib, platform, subprocess, sys, tarfile, urllib.request, zipfile
ROOT = pathlib.Path(__file__).resolve().parents[2]
os.chdir(ROOT)
OUT = ROOT / "artifacts/security"
OUT.mkdir(parents=True, exist_ok=True)

def run(command, output):
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    (OUT / output).write_text(result.stdout, encoding="utf-8")
    return result

def secrets():
    windows = platform.system() == "Windows"
    asset = "gitleaks_8.30.1_windows_x64.zip" if windows else "gitleaks_8.30.1_linux_x64.tar.gz"
    expected = "d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e" if windows else "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb"
    archive = OUT / asset
    if not archive.exists():
        archive.write_bytes(urllib.request.urlopen("https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/" + asset, timeout=60).read())
    data = archive.read_bytes()
    if hashlib.sha256(data).hexdigest() != expected: raise RuntimeError("Scanner checksum mismatch")
    binary = OUT / ("gitleaks.exe" if windows else "gitleaks")
    if windows:
        with zipfile.ZipFile(io.BytesIO(data)) as package: binary.write_bytes(package.read("gitleaks.exe"))
    else:
        with tarfile.open(fileobj=io.BytesIO(data)) as package: binary.write_bytes(package.extractfile("gitleaks").read())
        binary.chmod(0o755)
    # No rule suppression/allowlists. Fetch-depth:0 in CI; include every available ref.
    result = subprocess.run([str(binary), "git", "--log-opts=--all", "--redact", "--report-format=json", "--report-path=" + str(OUT / "history.json")])
    if result.returncode: raise RuntimeError("Secret scan failed; inspect redacted local report")

def dependencies():
    result = run(["dotnet", "list", "IndustrialMaintenanceCopilot.slnx", "package", "--vulnerable", "--include-transitive", "--format", "json"], "nuget.json")
    if result.returncode: raise RuntimeError("NuGet scan unavailable")
    data = json.loads(result.stdout)
    if not isinstance(data.get("projects"), list) or not data["projects"] or any(log.get("level", "").lower() == "error" for log in data.get("logs", [])): raise RuntimeError("Incomplete NuGet scan")
    findings = []
    for project in data.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ["topLevelPackages", "transitivePackages"]:
                for package in framework.get(kind, []):
                    for vulnerability in package.get("vulnerabilities", []):
                        if vulnerability["severity"].lower() in ["high", "critical"]: findings.append(package["id"])
    # Includes development dependencies: builds and browser tests are part of the supply chain.
    result = run(["npm.cmd" if os.name == "nt" else "npm", "audit", "--json", "--prefix", "src/IndustrialCopilot.Web"], "npm.json")
    audit = json.loads(result.stdout)
    if "error" in audit or "metadata" not in audit: raise RuntimeError("npm audit unavailable")
    counts = audit["metadata"]["vulnerabilities"]
    if findings or counts.get("high", 0) or counts.get("critical", 0): raise RuntimeError("High/critical dependency vulnerabilities detected")
    if result.returncode not in [0, 1]: raise RuntimeError("npm audit failed")
    print("No high/critical NuGet or npm findings. npm counts:", counts)

if __name__ == "__main__":
    try:
        if sys.argv[1:] == ["secrets"]: secrets()
        elif sys.argv[1:] == ["dependencies"]: dependencies()
        else: raise RuntimeError("Use secrets or dependencies")
    except Exception as error:
        # Never dump subprocess environment or scanner finding values.
        print("Security gate failed:", type(error).__name__)
        sys.exit(1)
