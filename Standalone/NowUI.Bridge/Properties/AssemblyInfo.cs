// The bridge is a browser-only assembly: every [JSImport] in it needs the browser runtime pack. Declared here
// rather than per-call, exactly as NowUI.Web does, so CA1416 checks the whole assembly against the right platform.
[assembly:System.Runtime.Versioning.SupportedOSPlatform("browser")]
