namespace BackupMonitor.Core.Models
{
    public enum ServiceCheckMode
    {
        NameDate,
        FileTime
    }

    public enum FileTimeSource
    {
        LastWriteTime,
        CreationTime
    }

    public enum ServiceType
    {
        Single,
        Group
    }

    public enum ServiceCheckStatus
    {
        OK,
        WARNING,
        FAIL,
        ERROR
    }
}
