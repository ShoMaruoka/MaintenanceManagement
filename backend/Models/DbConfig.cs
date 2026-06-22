namespace MaintenanceManagement.Api.Models;

public class DbConfig
{
    public string Name { get; set; } = "";
    public string DevDb { get; set; } = "";
    public string PrdDb { get; set; } = "";
    public string SqlServerConnectionString { get; set; } = "";
    public string MariaDbConnectionString { get; set; } = "";
    public string SourceControlPath { get; set; } = "";
    public string GitRepoPath { get; set; } = "";
    public string DeployDev2StgPath { get; set; } = "";
    public string Deploy2PrdPath { get; set; } = "";

    public string MergePath => Path.Combine(SourceControlPath, "merge");
    public string DeploySourcePath => Path.Combine(DeployDev2StgPath, "ForNewCreation", "Source");
    public string DeployedPath => Path.Combine(DeploySourcePath, "deployed");
    public string DeployedHoldPath => Path.Combine(DeploySourcePath, "deployed_hold");
    public string MariaDbSourcePath => Path.Combine(DeployDev2StgPath, "MariaDB");
    public string MariaDbDeployedPath => Path.Combine(MariaDbSourcePath, "deployed");
    public string MariaDbDeployedHoldPath => Path.Combine(MariaDbSourcePath, "deployed_hold");
}
