namespace KoreEngine.Hub.Services;

/// <summary>
/// Références NuGet SDL3 écrites dans les .csproj générés (projet, Player, build).
/// Deux ItemGroup conditionnés par l'OS de build : un projet créé par le Hub compile
/// ainsi aussi bien sous Windows que sous Linux (même principe que KoreEngine.Runtime).
/// Le paquet de base « SDL3-CS » reste dans le .csproj généré lui-même.
/// </summary>
static class SdlPackages
{
    public static string PlatformItemGroups(bool includeShadercross)
    {
        string windowsShadercross = includeShadercross
            ? "\n        <PackageReference Include=\"SDL3-CS.Windows.Shadercross\" Version=\"3.0.0.2\" />" : "";
        string linuxShadercross = includeShadercross
            ? "\n        <PackageReference Include=\"SDL3-CS.Linux.Shadercross\" Version=\"3.0.0.2\" />" : "";

        return
$@"    <ItemGroup Condition=""'$(OS)' == 'Windows_NT'"">
        <PackageReference Include=""SDL3-CS.Windows"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Image"" Version=""3.4.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Mixer"" Version=""3.2.4.2"" />{windowsShadercross}
        <PackageReference Include=""SDL3-CS.Windows.TTF"" Version=""3.2.2.2"" />
    </ItemGroup>

    <ItemGroup Condition=""'$(OS)' != 'Windows_NT'"">
        <PackageReference Include=""SDL3-CS.Linux"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Linux.Image"" Version=""3.4.4.2"" />
        <PackageReference Include=""SDL3-CS.Linux.Mixer"" Version=""3.2.4.2"" />{linuxShadercross}
        <PackageReference Include=""SDL3-CS.Linux.TTF"" Version=""3.2.2.2"" />
    </ItemGroup>";
    }
}