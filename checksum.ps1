# zipファイルのMD5チェックサムを計算する関数
function Get-MD5Checksum {
    param (
        [string]$FilePath
    )
    
    $md5 = New-Object -TypeName System.Security.Cryptography.MD5CryptoServiceProvider
    $hash = [System.BitConverter]::ToString($md5.ComputeHash([System.IO.File]::ReadAllBytes($FilePath)))
    return $hash.Replace("-", "").ToLower()
}

# 使用例
$zipPath = "JellyfinWbProvider.zip"

# チェックサムを計算
$checksum = Get-MD5Checksum -FilePath $zipPath
Write-Host "MD5 Checksum: $checksum"