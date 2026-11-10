using System.Reflection;

// exe 的「属性 → 详细信息」里能看到出处。
[assembly: AssemblyTitle("Just Say Everything")]
[assembly: AssemblyProduct("Just Say Everything")]
[assembly: AssemblyDescription("按住说一句，电脑就去做 · https://github.com/707717997/just-say-everything")]
[assembly: AssemblyCompany("707717997")]
[assembly: AssemblyCopyright("Copyright 2026 707717997 · Apache-2.0")]
[assembly: AssemblyVersion("0.2.2.0")]
[assembly: AssemblyFileVersion("0.2.2.0")]
[assembly: AssemblyInformationalVersion("0.2.2+jse-7077")]

namespace SayAllVoice
{
    /// <summary>出处信息：启动日志、托盘「关于」和「你是谁」都从这里取。</summary>
    public static class Origin
    {
        public const string Name = "Just Say Everything";
        public const string Author = "707717997";
        public const string Url = "https://github.com/707717997/just-say-everything";
        public const string License = "Apache-2.0";
        public const string Mark = "jse-7077-a1f3";

        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }
    }
}
