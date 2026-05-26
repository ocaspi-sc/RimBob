param(
    [int64]$ThresholdBytes = 102400,
    [string]$Ref = "HEAD"
)

$ErrorActionPreference = "Stop"

$largeBlobs = @(
    git ls-tree -r -l $Ref |
        ForEach-Object {
            $parts = $_ -split "`t", 2
            if ($parts.Count -lt 2) {
                return
            }

            $meta = $parts[0] -split "\s+"
            if ($meta.Count -lt 4 -or $meta[3] -eq "-") {
                return
            }

            $sizeBytes = [int64]$meta[3]
            if ($sizeBytes -ge $ThresholdBytes) {
                [pscustomobject]@{
                    SizeBytes = $sizeBytes
                    Path = $parts[1]
                }
            }
        }
)

if ($largeBlobs.Count -eq 0) {
    Write-Host "No committed Git blobs >= $ThresholdBytes bytes."
    exit 0
}

Write-Host "ERROR: Committed Git blobs >= $ThresholdBytes bytes must be stored as Git LFS pointers."
$largeBlobs |
    Sort-Object SizeBytes -Descending |
    ForEach-Object {
        $sizeKiB = [math]::Round($_.SizeBytes / 1KB, 1)
        Write-Host ("{0,8} KiB  {1}" -f $sizeKiB, $_.Path)
    }

exit 1
