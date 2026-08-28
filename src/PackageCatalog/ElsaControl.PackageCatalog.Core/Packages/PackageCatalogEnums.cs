namespace ElsaControl.PackageCatalog.Core.Packages;

public enum PackageSourceType
{
    NuGetFeed
}

public enum PackageSourceApprovalPolicy
{
    AutoApprove,
    Manual
}

public enum PackageSourceVersionDiscoveryPolicy
{
    AllVersions,
    LatestStable,
    LatestIncludingPrerelease,
    LatestPreview
}

public enum PackageSourceStatus
{
    Healthy,
    Warning,
    Error
}

public enum PackageApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public enum ValidationStatus
{
    NotValidated,
    Valid,
    Invalid,
    UnsupportedSchema,
    Suspicious
}

public enum SyncRunTrigger
{
    Scheduled,
    ManualAll,
    ManualSource,
    ManualPackage
}

public enum SyncRunStatus
{
    Running,
    Completed,
    Failed,
    CompletedWithErrors,
    Canceled
}

public enum SyncRunItemStatus
{
    Discovered,
    Skipped,
    Downloaded,
    Indexed,
    Unchanged,
    Invalid,
    Failed,
    Suspicious
}

public enum ApprovalTargetType
{
    Package,
    PackageVersion
}
