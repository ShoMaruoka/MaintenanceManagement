namespace MaintenanceManagement.Api.Models;

public class DbConfig
{
    /// <summary>
    /// システム識別名（例: kaios / gos）。
    /// pilot 用 Web.config のファイル名（Web.config.DC.{Name}.pilot）の導出にも使用される。
    /// </summary>
    public string Name { get; set; } = "";

    // DB 名（3 環境）
    public string DevDb { get; set; } = "";    // 開発DB（モジュール一覧の取得元）
    public string StgDb { get; set; } = "";    // STG DB（フェーズ1の適用先）
    public string PrdDb { get; set; } = "";    // 本番DB（フェーズ2用・現在未使用）

    // 接続文字列（3 環境）
    public string DevConnectionString { get; set; } = "";   // 開発DB への接続（モジュール一覧取得）
    /// <summary>
    /// STG DB への接続。操作区分の新規／更新判定（STG 上のオブジェクト存在確認）に使用する。
    /// 未設定時は Git ファイル存在チェックにフォールバックする。
    /// </summary>
    public string StgConnectionString { get; set; } = "";
    public string PrdConnectionString { get; set; } = "";   // 本番DB への接続（フェーズ2用・現在未使用）

    // MariaDB（DevDB のみ対象）
    public string MariaDbConnectionString { get; set; } = "";

    /// <summary>MariaDB 用 Git リポジトリのパス（SQL Server の GitRepoPath とは別リポジトリ）。</summary>
    public string MariaDbGitRepoPath { get; set; } = "";

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

    /// <summary>
    /// Table / UserDefinedTableType の手動適用待ち置き場。
    /// deploy.bat は Source\*.sql を非再帰で拾うため、サブフォルダに置くことで自動適用を防ぐ。
    /// </summary>
    public string DeployedManualPath => Path.Combine(DeploySourcePath, "deployed_manual");

    /// <summary>手動適用待ち項目のメタ情報（1 行 1 JSON）。</summary>
    public string DeployedManualManifestPath => Path.Combine(DeployedManualPath, "manifest.jsonl");

    /// <summary>手動適用対象 SQL の本番受け渡し先。本番側 deploy.bat の対象外となるサブフォルダ。</summary>
    public string ManualApplyDeploy2PrdPath => Path.Combine(Deploy2PrdPath, "ManualApply");
    public string MariaDbSourcePath => Path.Combine(DeployDev2StgPath, "MariaDB");
    public string MariaDbDeployedPath => Path.Combine(MariaDbSourcePath, "deployed");
    public string MariaDbDeployedHoldPath => Path.Combine(MariaDbSourcePath, "deployed_hold");

    /// <summary>MariaDB 用 UpdateModule.txt / DeleteModule.txt の出力先（merge と同階層）。</summary>
    public string MariaDbMergePath => Path.Combine(SourceControlPath, "merge_MariaDB");
    /// <summary>MariaDB 用 deploy.bat・適用対象 SQL の配置ルート。</summary>
    public string MariaDbForNewCreationPath => Path.Combine(MariaDbSourcePath, "ForNewCreation");
    /// <summary>MariaDB 用 SQL 変換後の適用対象コピー先。</summary>
    public string MariaDbDeploySourcePath => Path.Combine(MariaDbForNewCreationPath, "Source");
    /// <summary>MariaDB 用 deploy.bat（mysql CLI 呼び出し）のパス。事前配置・本システムは作成しない。</summary>
    public string MariaDbDeployBatPath => Path.Combine(MariaDbForNewCreationPath, "deploy.bat");
    /// <summary>STG 側静的ファイル保管先（DeployDev2StgPath\Files）。</summary>
    public string FilesPath => Path.Combine(DeployDev2StgPath, "Files");

    // Web ソース配布（STG → pilot、Issue #25）
    /// <summary>STG側 IIS 公開フォルダ（Webソースのコピー元）。</summary>
    public string WebSourcePath { get; set; } = "";
    /// <summary>適用先 pilot サーバー一覧（pilot1 → pilot2 の順で適用）。</summary>
    public List<PilotTarget> PilotTargets { get; set; } = [];

    /// <summary>
    /// pilot 環境向け SQL Server 適用フォルダ（配下に "Source" と "deploy.bat"。本システムは bat を作成しない）。
    /// SQL Server（DB）は pilot1/pilot2 で共有のため、PilotTargets とは別に DB 単位で1パスのみ保持する。
    /// </summary>
    public string PilotSqlDeployPath { get; set; } = "";

    /// <summary>PilotSqlDeployPath 配下の SQL Server 用コピー先（DeployedPath の *.sql）。</summary>
    public string PilotSqlDeploySourcePath => Path.Combine(PilotSqlDeployPath, "Source");

    /// <summary>PilotSqlDeployPath 配下の SQL Server 適用バッチ（事前配置）。</summary>
    public string PilotSqlDeployBatPath => Path.Combine(PilotSqlDeployPath, "deploy.bat");

    /// <summary>
    /// pilot 環境向け MariaDB 適用フォルダ（配下に "Source" と "deploy.bat"。本システムは bat を作成しない）。
    /// STG の MariaDbForNewCreationPath と同様、SQL Server 用とは別ツリー（Issue #35 B1）。
    /// </summary>
    public string PilotMariaDbSqlDeployPath { get; set; } = "";

    /// <summary>PilotMariaDbSqlDeployPath 配下の MariaDB 用コピー先（MariaDbDeployedPath の *.sql）。</summary>
    public string PilotMariaDbSqlDeploySourcePath => Path.Combine(PilotMariaDbSqlDeployPath, "Source");

    /// <summary>PilotMariaDbSqlDeployPath 配下の MariaDB 適用バッチ（事前配置）。</summary>
    public string PilotMariaDbSqlDeployBatPath => Path.Combine(PilotMariaDbSqlDeployPath, "deploy.bat");

    // 画像コピー・View DB 名置換（Issue #27）
    /// <summary>STG 側の共通画像フォルダ（コピー元）。未設定時は画像コピーステップをスキップする。</summary>
    public string CommonImagePath { get; set; } = "";

    /// <summary>
    /// pilot 向け SQL 適用時に View ソース内の DB 名を置換するルール一覧。
    /// 未設定（空）の場合は置換ステップ自体をスキップする。
    /// </summary>
    public List<PilotDbNameReplacement> PilotSqlDbNameReplacements { get; set; } = [];
}

/// <summary>pilot サーバー1台分の適用先情報。</summary>
public class PilotTarget
{
    public string Name { get; set; } = "";
    /// <summary>コピー先パス（DbConfig.WebSourcePath=STG側と混同しないよう Dest を付与）。</summary>
    public string DestWebSourcePath { get; set; } = "";
    /// <summary>共通画像のコピー先（例: ...\Images\products）。未設定時は画像コピーステップをスキップする。</summary>
    public string DestImagePath { get; set; } = "";
}

/// <summary>pilot 向け SQL 適用時の View ソース内 DB 名置換ルール（例: KaiosDB → KaiosDB_pilot）。</summary>
public class PilotDbNameReplacement
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
