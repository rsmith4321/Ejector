# Supplementary OS-provider diagnostics, not proof that the prototype's P/Invoke layer works.
# This script contains no writes, imports, dismounts, or removal requests.
$ErrorActionPreference = 'Stop'
$disks = @(Get-CimInstance Win32_DiskDrive | Select-Object Index,InterfaceType,MediaType,Size)
$volumes = @(Get-CimInstance Win32_Volume | Select-Object DeviceID,DriveLetter,FileSystem,Capacity)
$partitions = @(Get-CimInstance Win32_DiskPartition | Select-Object DiskIndex,Index,Size,BootPartition,Type)
[pscustomobject]@{
    ReadOnly = $true
    Disks = $disks
    Volumes = $volumes
    Partitions = $partitions
    DiskPartitionAssociations = @(Get-CimInstance Win32_DiskDriveToDiskPartition).Count
    LogicalDiskAssociations = @(Get-CimInstance Win32_LogicalDiskToPartition).Count
} | ConvertTo-Json -Depth 5
