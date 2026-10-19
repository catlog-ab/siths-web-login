using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace SithsWebLogin
{
    public class SignResult
    {
        public string CertDer;     // base64
        public string Signature;   // base64
        public string Alg;         // RS256 / ES256
        public string Thumbprint;
    }

    // Hittar kortets SITHS-autentiseringscertifikat i Windows certifikatlager och signerar
    // inloggningsutmaningen med kortets privata nyckel. Net iD/SITHS eID visar PIN-dialogen.
    public static class CardSigner
    {
        const string OidClientAuth = "1.3.6.1.5.5.7.3.2";

        // Exakt den sträng som servern bygger upp och verifierar, se SithsVerifier::signedPayload().
        public static byte[] Payload(string domain, string nonce)
        {
            return Encoding.UTF8.GetBytes("siths-web-login|" + domain + "|" + nonce);
        }

        public static SignResult Sign(string domain, string nonce)
        {
            byte[] data = Payload(domain, nonce);

            // Certifikatspridningen i Windows tar en stund efter att kortet satts i.
            List<X509Certificate2> certs = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                certs = FindAuthCertificates();
                if (certs.Count > 0) break;
                Thread.Sleep(500);
            }
            if (certs == null || certs.Count == 0)
                throw new Exception("no_certificate");

            Exception last = null;
            foreach (X509Certificate2 cert in certs)
            {
                try
                {
                    SignResult r = new SignResult();
                    r.CertDer = Convert.ToBase64String(cert.RawData);
                    r.Thumbprint = cert.Thumbprint;

                    RSA rsa = cert.GetRSAPrivateKey();
                    if (rsa != null)
                    {
                        r.Signature = Convert.ToBase64String(SignRsa(rsa, data));
                        r.Alg = "RS256";
                        return r;
                    }
                    ECDsa ec = cert.GetECDsaPrivateKey();
                    if (ec != null)
                    {
                        // Servern förväntar sig DER-kodad ECDSA-signatur (som openssl_verify).
                        r.Signature = Convert.ToBase64String(EcdsaP1363ToDer(ec.SignData(data, HashAlgorithmName.SHA256)));
                        r.Alg = "ES256";
                        return r;
                    }
                }
                catch (CryptographicException e)
                {
                    // Avbruten PIN-dialog eller låst kort: försök inte med nästa certifikat.
                    if (IsCancelled(e)) throw new Exception("pin_cancelled");
                    if (IsPinBlocked(e)) throw new Exception("pin_blocked");
                    // T.ex. certifikat kvar i lagret från ett kort som inte sitter i.
                    last = e;
                }
            }
            throw new Exception(last != null
                ? "sign_failed: 0x" + last.HResult.ToString("X8") + " " + last.Message
                : "sign_failed");
        }

        static byte[] SignRsa(RSA rsa, byte[] data)
        {
            return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        static bool IsCancelled(CryptographicException e)
        {
            int h = e.HResult;
            return h == unchecked((int)0x8010006E)  // SCARD_W_CANCELLED_BY_USER
                || h == unchecked((int)0x800704C7)  // ERROR_CANCELLED
                || h == unchecked((int)0x80100002)  // SCARD_E_CANCELLED
                || h == 0x4C7;
        }

        static bool IsPinBlocked(CryptographicException e)
        {
            return e.HResult == unchecked((int)0x8010006C); // SCARD_W_CHV_BLOCKED
        }

        public static List<X509Certificate2> FindAuthCertificates()
        {
            List<X509Certificate2> found = new List<X509Certificate2>();
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            try
            {
                DateTime now = DateTime.Now;
                foreach (X509Certificate2 c in store.Certificates)
                {
                    if (!c.HasPrivateKey) continue;
                    if (now < c.NotBefore || now > c.NotAfter) continue;
                    if (c.Issuer.IndexOf("SITHS", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Endast HSA-id-certifikatet; certifikatet med personnummer ska inte användas.
                    if (c.Issuer.IndexOf("HSA-id", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!HasDigitalSignature(c) || !HasClientAuth(c)) continue;
                    found.Add(c);
                }
            }
            finally
            {
                store.Close();
            }
            found.Sort(delegate (X509Certificate2 a, X509Certificate2 b) { return b.NotBefore.CompareTo(a.NotBefore); });
            return found;
        }

        static bool HasDigitalSignature(X509Certificate2 c)
        {
            foreach (X509Extension e in c.Extensions)
            {
                X509KeyUsageExtension ku = e as X509KeyUsageExtension;
                if (ku != null)
                    return (ku.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0;
            }
            return false;
        }

        static bool HasClientAuth(X509Certificate2 c)
        {
            foreach (X509Extension e in c.Extensions)
            {
                X509EnhancedKeyUsageExtension eku = e as X509EnhancedKeyUsageExtension;
                if (eku == null) continue;
                foreach (Oid o in eku.EnhancedKeyUsages)
                    if (o.Value == OidClientAuth) return true;
                return false;
            }
            return false;
        }

        static byte[] EcdsaP1363ToDer(byte[] sig)
        {
            int half = sig.Length / 2;
            byte[] r = DerInt(sig, 0, half), s = DerInt(sig, half, half);
            List<byte> body = new List<byte>();
            body.AddRange(r);
            body.AddRange(s);
            List<byte> outp = new List<byte>();
            outp.Add(0x30);
            outp.AddRange(DerLen(body.Count));
            outp.AddRange(body);
            return outp.ToArray();
        }

        static byte[] DerInt(byte[] src, int off, int len)
        {
            int start = off;
            while (start < off + len - 1 && src[start] == 0) start++;
            List<byte> v = new List<byte>();
            if ((src[start] & 0x80) != 0) v.Add(0);
            for (int i = start; i < off + len; i++) v.Add(src[i]);
            List<byte> outp = new List<byte>();
            outp.Add(0x02);
            outp.AddRange(DerLen(v.Count));
            outp.AddRange(v);
            return outp.ToArray();
        }

        static byte[] DerLen(int n)
        {
            return n < 128 ? new byte[] { (byte)n } : new byte[] { 0x81, (byte)n };
        }
    }
}
