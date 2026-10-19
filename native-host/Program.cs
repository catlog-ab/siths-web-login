using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("SITHS Web Login")]
[assembly: AssemblyProduct("SITHS Web Login")]
[assembly: AssemblyDescription("Native messaging host for SITHS card login in Chrome/Edge")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SithsWebLogin
{
    public static class Program
    {
        public const string Version = "1.0.0";
        const string ExeName = "SithsWebLogin.exe";

        static readonly object writeLock = new object();
        static Stream stdout;
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        static CardMonitor monitor;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("chrome-extension://"))
                return RunHost();
            if (args.Length > 0 && args[0] == "--uninstall")
                return Uninstall(args.Length > 1 && args[1] == "--quiet");
            if (args.Length > 0 && args[0] == "--selftest")
                return SelfTest();
            return Install(args.Length > 0 && args[0] == "--quiet");
        }

        // ---------------- Native messaging ----------------
        // Chrome kontrollerar redan att anroparen finns i allowed_origins i host-manifestet.

        static int RunHost()
        {
            Stream stdin = Console.OpenStandardInput();
            stdout = Console.OpenStandardOutput();

            monitor = new CardMonitor();
            monitor.CardChanged += delegate (bool present, string reader, string atr)
            {
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["type"] = present ? "card_inserted" : "card_removed";
                m["reader"] = reader;
                m["atr"] = atr;
                Send(m);
            };
            monitor.Start();

            while (true)
            {
                Dictionary<string, object> msg = Read(stdin);
                if (msg == null) return 0; // Chrome stängde porten.
                Dictionary<string, object> request = msg;
                ThreadPool.QueueUserWorkItem(delegate { Handle(request); });
            }
        }

        static void Handle(Dictionary<string, object> msg)
        {
            object idObj;
            msg.TryGetValue("id", out idObj);
            string cmd = msg.ContainsKey("cmd") ? Convert.ToString(msg["cmd"]) : "";
            Dictionary<string, object> reply = new Dictionary<string, object>();
            reply["type"] = "reply";
            reply["id"] = idObj;
            try
            {
                if (cmd == "status")
                {
                    reply["cardPresent"] = monitor.CardPresent;
                    reply["reader"] = monitor.CurrentReader;
                    reply["version"] = Version;
                }
                else if (cmd == "sign")
                {
                    if (!monitor.CardPresent) throw new Exception("no_card");
                    string domain = msg.ContainsKey("domain") ? Convert.ToString(msg["domain"]) : "";
                    string nonce = msg.ContainsKey("nonce") ? Convert.ToString(msg["nonce"]) : "";
                    if (domain.Length == 0 || domain.Length > 253 || nonce.Length == 0 || nonce.Length > 200)
                        throw new Exception("bad_request");
                    SignResult r = CardSigner.Sign(domain, nonce);
                    reply["cert"] = r.CertDer;
                    reply["signature"] = r.Signature;
                    reply["alg"] = r.Alg;
                    reply["thumbprint"] = r.Thumbprint;
                }
                else
                {
                    throw new Exception("unknown_command");
                }
                reply["ok"] = true;
            }
            catch (Exception e)
            {
                reply["ok"] = false;
                reply["error"] = e.Message;
            }
            Send(reply);
        }

        static Dictionary<string, object> Read(Stream s)
        {
            byte[] lenBuf = ReadExact(s, 4);
            if (lenBuf == null) return null;
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0 || len > 1024 * 1024) return null;
            byte[] body = ReadExact(s, len);
            if (body == null) return null;
            try
            {
                return json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body));
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        static byte[] ReadExact(Stream s, int n)
        {
            byte[] buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) return null;
                off += r;
            }
            return buf;
        }

        static void Send(Dictionary<string, object> m)
        {
            byte[] body = Encoding.UTF8.GetBytes(json.Serialize(m));
            lock (writeLock)
            {
                stdout.Write(BitConverter.GetBytes(body.Length), 0, 4);
                stdout.Write(body, 0, body.Length);
                stdout.Flush();
            }
        }

        // ---------------- Installation ----------------
        // Allt hamnar under %LOCALAPPDATA%\SithsWebLogin\<hostName>\ och HKCU, så ingen admin behövs
        // och flera organisationer kan ha var sin installation på samma dator.

        static string InstallDir(HostConfig c)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SithsWebLogin", c.HostName);
        }

        static string[] BrowserKeys(HostConfig c)
        {
            return new string[]
            {
                "Software\\Google\\Chrome\\NativeMessagingHosts\\" + c.HostName,
                "Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + c.HostName,
            };
        }

        // Webbläsarna hämtar tillägget från butiken och frågar användaren om det ska aktiveras.
        // Nyckel -> update_url. Tom lista om storeExtensions inte är satt.
        static Dictionary<string, string> StoreExtensionKeys(HostConfig c)
        {
            Dictionary<string, string> keys = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(c.ChromeStoreId))
                keys["Software\\Google\\Chrome\\Extensions\\" + c.ChromeStoreId] = "https://clients2.google.com/service/update2/crx";
            if (!string.IsNullOrEmpty(c.EdgeStoreId))
                keys["Software\\Microsoft\\Edge\\Extensions\\" + c.EdgeStoreId] = "https://edge.microsoft.com/extensionwebstorebase/v1/crx";
            return keys;
        }

        static string UninstallKey(HostConfig c)
        {
            return "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SithsWebLogin_" + c.HostName;
        }

        static int Install(bool quiet)
        {
            string title = "SITHS Web Login";
            try
            {
                string self = Assembly.GetExecutingAssembly().Location;
                HostConfig c = HostConfig.Load(HostConfig.PathNextTo(self));
                title = c.DisplayName;

                string dir = InstallDir(c);
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, ExeName);
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                {
                    // Om en gammal version körs av Chrome just nu kan filen vara låst; byt namn först.
                    if (File.Exists(exe))
                    {
                        string old = exe + ".old";
                        try { if (File.Exists(old)) File.Delete(old); } catch (Exception) { }
                        File.Move(exe, old);
                    }
                    File.Copy(self, exe, true);
                    File.Copy(HostConfig.PathNextTo(self), Path.Combine(dir, HostConfig.FileName), true);
                }

                List<string> origins = new List<string>();
                foreach (string id in c.AllowedExtensionIds) origins.Add("chrome-extension://" + id + "/");
                Dictionary<string, object> manifest = new Dictionary<string, object>();
                manifest["name"] = c.HostName;
                manifest["description"] = c.DisplayName;
                manifest["path"] = exe;
                manifest["type"] = "stdio";
                manifest["allowed_origins"] = origins;
                string manifestPath = Path.Combine(dir, c.HostName + ".json");
                File.WriteAllText(manifestPath, json.Serialize(manifest), new UTF8Encoding(false));

                foreach (string key in BrowserKeys(c))
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
                        k.SetValue("", manifestPath);

                Dictionary<string, string> storeKeys = StoreExtensionKeys(c);
                foreach (KeyValuePair<string, string> e in storeKeys)
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(e.Key))
                        k.SetValue("update_url", e.Value);

                // Avinstallation via "Appar" i Windows-inställningarna.
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey(c)))
                {
                    k.SetValue("DisplayName", c.DisplayName);
                    k.SetValue("Publisher", "SITHS Web Login");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
                    k.SetValue("NoModify", 1);
                    k.SetValue("NoRepair", 1);
                }

                if (!quiet)
                    MessageBox.Show(
                        c.DisplayName + " är installerat.\n\n" +
                        (storeKeys.Count > 0
                            ? "Starta om webbläsaren. Klicka på \"Aktivera\" när den frågar om det nya tillägget."
                            : "Kontrollera att webbläsartillägget är installerat.") +
                        " Du loggas sedan in när SITHS-kortet sitter i läsaren och loggas ut när du tar ur det.",
                        title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception e)
            {
                if (!quiet)
                    MessageBox.Show("Installationen misslyckades:\n" + e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Uninstall(bool quiet)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            HostConfig c;
            try
            {
                c = HostConfig.Load(HostConfig.PathNextTo(self));
            }
            catch (Exception e)
            {
                if (!quiet) MessageBox.Show("Avinstallationen misslyckades:\n" + e.Message, "SITHS Web Login", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // Att nyckeln för butikstillägget försvinner får webbläsaren att ta bort tillägget också.
            List<string> keys = new List<string>(BrowserKeys(c));
            keys.AddRange(StoreExtensionKeys(c).Keys);
            keys.Add(UninstallKey(c));
            foreach (string key in keys)
                try { Registry.CurrentUser.DeleteSubKeyTree(key, false); } catch (Exception) { }

            string dir = InstallDir(c);
            foreach (string f in new string[] { c.HostName + ".json", HostConfig.FileName, ExeName + ".old" })
                try { File.Delete(Path.Combine(dir, f)); } catch (Exception) { }
            // Exe-filen kan inte ta bort sig själv medan den körs; låt cmd göra det efteråt.
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 /nobreak >nul & del /q \"" + Path.Combine(dir, ExeName) + "\" & rmdir \"" + dir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception) { }

            if (!quiet)
                MessageBox.Show(c.DisplayName + " är avinstallerat.", c.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // ---------------- Självtest ----------------

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        static int SelfTest()
        {
            if (!AttachConsole(-1)) AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
            Console.WriteLine();
            Console.WriteLine("SITHS Web Login " + Version + " självtest. Sätt i/ta ur kortet. S = provsignering, Q = avsluta.");

            foreach (var c in CardSigner.FindAuthCertificates())
                Console.WriteLine("Certifikat: " + c.Subject + "  (" + c.Thumbprint + ")");

            monitor = new CardMonitor();
            monitor.CardChanged += delegate (bool present, string reader, string atr)
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + (present ? "  KORT IN  " + reader + "  ATR " + atr : "  KORT UR"));
            };
            monitor.Start();

            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Q) return 0;
                if (k.Key == ConsoleKey.S)
                {
                    try
                    {
                        string nonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                        SignResult r = CardSigner.Sign("selftest.invalid", nonce);
                        Console.WriteLine("Signerat med " + r.Alg + ", certifikat " + r.Thumbprint + ", signatur " + r.Signature.Length + " tecken.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Signering misslyckades: " + e.Message);
                    }
                }
            }
        }
    }
}
