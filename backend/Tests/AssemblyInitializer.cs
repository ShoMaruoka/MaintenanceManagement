using System.Runtime.CompilerServices;
using System.Text;

namespace MaintenanceManagement.Api.Tests;

/// <summary>
/// SJIS(CP932) 等のコードページはこの登録がないと Encoding.GetEncoding が失敗する。
/// 本番アプリは Program.cs で登録済みだが、テストプロジェクトはそれを経由しないため個別に登録する。
/// </summary>
internal static class AssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
