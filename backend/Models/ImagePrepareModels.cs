namespace MaintenanceManagement.Api.Models;

/// <summary>画像情報準備: DB 単位の Files ツリー応答。</summary>
public class ImagePrepareTreeResponse
{
    public string DbName { get; set; } = "";
    public List<ImageCategoryNode> Categories { get; set; } = [];
}

/// <summary>Images / news / pdf のいずれかのルートノード。</summary>
public class ImageCategoryNode
{
    public string Name { get; set; } = "";
    public List<ImageTreeEntry> Entries { get; set; } = [];
}

/// <summary>フォルダまたはファイル。</summary>
public class ImageTreeEntry
{
    public string Name { get; set; } = "";
    /// <summary>Files ルートからの相対パス（区切りは /）。例: Images/flash/img/a.png</summary>
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public List<ImageTreeEntry> Children { get; set; } = [];
}

public class ImageCreateFolderRequest
{
    public string Category { get; set; } = "";
    public string RelativeSubPath { get; set; } = "";
}

public class ImageCreateFolderResponse
{
    public string DbName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool DryRun { get; set; }
    public bool Created { get; set; }
}

public class ImageUploadSavedFile
{
    public string RelativePath { get; set; } = "";
    public bool Overwritten { get; set; }
}

public class ImageUploadResponse
{
    public string DbName { get; set; } = "";
    public bool DryRun { get; set; }
    public List<ImageUploadSavedFile> Saved { get; set; } = [];
}
