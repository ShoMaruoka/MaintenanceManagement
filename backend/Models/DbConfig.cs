namespace MaintenanceManagement.Api.Models;

public class DbConfig
{
    public string Name { get; set; } = "";

    // DB 名（3 環境）
    public string DevDb { get; set; } = "";    // 開発DB（モジュール一覧の取得元）
    public string StgDb { get; set; } = "";    // STG DB（フェーズ1の適用先）
    public string PrdDb { get; set; } = "";    // 本番DB（フェーズ2用・現在未使用）

    // 接続文字列（3 環境）
    public string DevConnectionString { get; set; } = "";   // 開発DB への接続（モジュール一覧取得）
    public string StgConnectionString { get; set; } = "";   // STG DB への接続（フェーズ2用・現在未使用）
    public string PrdConnectionString { get; set; } = "";   // 本番DB への接続（フェーズ2用・現在未使用）

    // MariaDB（DevDB のみ対象）
    public string MariaDbConnectionString { get; set; } = "";

    // ファイルパス
    public string SourceControlPath { get; set; } = "";
    public string GitRepoPath { get; set; } = "";
    public string DeployDev2StgPath { get; set; } = "";
    public string Deploy2PrdPath { get; set; } = "";
    /// <summary>静的ファイル（Images/news/pdf）の本番移動先。SQL 用 Deploy2PrdPath とは別系統。</summary>
    public string FilesDeploy2PrdPath { get; set; } = "";

    public string MergePath => Path.Combine(SourceControlPath, "merge");
    public string ForNewCreationPath => Path.Combine(DeployDev2StgPath, "ForNewCreation");
    public string DeploySourcePath => Path.Combine(ForNewCreationPath, "Source");
    public string DeployedPath => Path.Combine(DeploySourcePath, "deployed");
    public string DeployedHoldPath => Path.Combine(DeploySourcePath, "deployed_hold");
    public string MariaDbSourcePath => Path.Combine(DeployDev2StgPath, "MariaDB");
    public string MariaDbDeployedPath => Path.Combine(MariaDbSourcePath, "deployed");
    public string MariaDbDeployedHoldPath => Path.Combine(MariaDbSourcePath, "deployed_hold");
    /// <summary>STG 側静的ファイル保管先（DeployDev2StgPath\Files）。</summary>
    public string FilesPath => Path.Combine(DeployDev2StgPath, "Files");
}
