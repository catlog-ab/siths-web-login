using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SithsWebLogin
{
    // Organisationens inställningar, från siths-host.json bredvid exe-filen.
    // Exe-filen är densamma för alla organisationer (och kan därför vara signerad);
    // allt som skiljer ligger i JSON-filen.
    public class HostConfig
    {
        public const string FileName = "siths-host.json";

        public string HostName;              // native messaging-namn, t.ex. "se.foretaget.siths"
        public string DisplayName;           // visas i installationsrutor och under Appar
        public List<string> AllowedExtensionIds = new List<string>();
        public string ChromeStoreId;         // valfritt; föreslås i Chrome vid installation
        public string EdgeStoreId;           // valfritt; föreslås i Edge vid installation

        public static string PathNextTo(string exePath)
        {
            return Path.Combine(Path.GetDirectoryName(exePath), FileName);
        }

        public static HostConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new Exception("Inställningsfilen " + FileName + " saknas bredvid programmet.\n\n" +
                    "Packa upp hela zip-filen (högerklicka → Extrahera alla) och starta programmet från den uppackade mappen.");
            Dictionary<string, object> d = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));

            HostConfig c = new HostConfig();
            c.HostName = Str(d, "hostName");
            c.DisplayName = Str(d, "displayName");
            object ids;
            if (d.TryGetValue("allowedExtensionIds", out ids) && ids is IEnumerable)
                foreach (object o in (IEnumerable)ids)
                    if (o != null) c.AllowedExtensionIds.Add(o.ToString());

            // Samma regler som Chrome: små bokstäver, siffror, punkt och understreck.
            if (c.HostName == null || !Regex.IsMatch(c.HostName, "^[a-z0-9_]+(\\.[a-z0-9_]+)*$"))
                throw new Exception("hostName i " + FileName + " är ogiltigt (t.ex. \"se.exempel.siths\").");
            if (c.AllowedExtensionIds.Count == 0)
                throw new Exception("allowedExtensionIds i " + FileName + " är tom.");
            foreach (string id in c.AllowedExtensionIds)
                if (!Regex.IsMatch(id, "^[a-p]{32}$"))
                    throw new Exception("Ogiltigt tilläggs-id i " + FileName + ": " + id);
            if (string.IsNullOrEmpty(c.DisplayName))
                c.DisplayName = "SITHS Web Login";

            // Valfritt: tillägg i Chrome Web Store / Edge Add-ons som webbläsaren ska föreslå vid installation.
            object store;
            if (d.TryGetValue("storeExtensions", out store) && store is Dictionary<string, object>)
            {
                Dictionary<string, object> s = (Dictionary<string, object>)store;
                c.ChromeStoreId = Str(s, "chrome");
                c.EdgeStoreId = Str(s, "edge");
            }
            foreach (string id in new string[] { c.ChromeStoreId, c.EdgeStoreId })
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!c.AllowedExtensionIds.Contains(id))
                    throw new Exception("storeExtensions i " + FileName + " innehåller " + id + " som saknas i allowedExtensionIds.");
            }
            return c;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }
    }
}
