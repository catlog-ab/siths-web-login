using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SithsWebLogin
{
    // Bevakar PC/SC-läsarna och rapporterar när ett kort sätts i eller tas ur.
    public class CardMonitor
    {
        const uint SCARD_SCOPE_USER = 0;
        const uint SCARD_STATE_UNAWARE = 0x0000;
        const uint SCARD_STATE_CHANGED = 0x0002;
        const uint SCARD_STATE_PRESENT = 0x0020;
        const uint SCARD_STATE_MUTE = 0x0200;
        const int SCARD_S_SUCCESS = 0;
        const int SCARD_E_TIMEOUT = unchecked((int)0x8010000A);
        const int SCARD_E_NO_READERS_AVAILABLE = unchecked((int)0x8010002E);
        const int SCARD_E_SERVICE_STOPPED = unchecked((int)0x8010001E);
        const int SCARD_E_NO_SERVICE = unchecked((int)0x8010001D);
        const string PnpReader = "\\\\?PnP?\\Notification";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SCARD_READERSTATE
        {
            public string szReader;
            public IntPtr pvUserData;
            public uint dwCurrentState;
            public uint dwEventState;
            public uint cbAtr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
            public byte[] rgbAtr;
        }

        [DllImport("winscard.dll")]
        static extern int SCardEstablishContext(uint dwScope, IntPtr r1, IntPtr r2, out IntPtr phContext);
        [DllImport("winscard.dll")]
        static extern int SCardReleaseContext(IntPtr hContext);
        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        static extern int SCardListReadersW(IntPtr hContext, string groups, char[] readers, ref int pcchReaders);
        [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
        static extern int SCardGetStatusChangeW(IntPtr hContext, uint dwTimeout, [In, Out] SCARD_READERSTATE[] states, int cReaders);

        // Anropas med (kortFinns, läsarnamn, ATR i hex).
        public event Action<bool, string, string> CardChanged;

        volatile bool cardPresent;
        volatile string currentReader;
        volatile string currentAtr;

        public bool CardPresent { get { return cardPresent; } }
        public string CurrentReader { get { return currentReader; } }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Name = "CardMonitor";
            t.Start();
        }

        void Run()
        {
            bool first = true;
            while (true)
            {
                IntPtr ctx;
                if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out ctx) != SCARD_S_SUCCESS)
                {
                    // Tjänsten "Smartkort" kör inte än, försök igen.
                    Publish(false, null, null, first);
                    first = false;
                    Thread.Sleep(2000);
                    continue;
                }
                try
                {
                    WatchLoop(ctx, ref first);
                }
                catch (Exception)
                {
                    Thread.Sleep(1000);
                }
                finally
                {
                    SCardReleaseContext(ctx);
                }
            }
        }

        void WatchLoop(IntPtr ctx, ref bool first)
        {
            while (true)
            {
                List<string> readers = ListReaders(ctx);
                SCARD_READERSTATE[] states = new SCARD_READERSTATE[readers.Count + 1];
                for (int i = 0; i < readers.Count; i++)
                {
                    states[i].szReader = readers[i];
                    states[i].dwCurrentState = SCARD_STATE_UNAWARE;
                    states[i].rgbAtr = new byte[36];
                }
                // Pseudo-läsaren som signalerar när läsare kopplas in eller ur.
                states[readers.Count].szReader = PnpReader;
                states[readers.Count].dwCurrentState = (uint)(readers.Count << 16);
                states[readers.Count].rgbAtr = new byte[36];

                bool readerListChanged = false;
                while (!readerListChanged)
                {
                    int rc = SCardGetStatusChangeW(ctx, 2000, states, states.Length);
                    if (rc == SCARD_E_TIMEOUT)
                        continue;
                    if (rc == SCARD_E_SERVICE_STOPPED || rc == SCARD_E_NO_SERVICE)
                    {
                        Publish(false, null, null, first);
                        first = false;
                        return; // Etablera ny kontext.
                    }
                    if (rc != SCARD_S_SUCCESS)
                    {
                        Thread.Sleep(500);
                        readerListChanged = true;
                        break;
                    }

                    for (int i = 0; i < states.Length; i++)
                    {
                        if ((states[i].dwEventState & SCARD_STATE_CHANGED) != 0 && i == readers.Count)
                            readerListChanged = true;
                        states[i].dwCurrentState = states[i].dwEventState & ~SCARD_STATE_CHANGED;
                    }

                    Evaluate(states, readers.Count, ref first);
                }
                // Utvärdera även efter att läsarlistan ändrats (t.ex. USB-läsaren drogs ur).
                if (ListReaders(ctx).Count == 0)
                {
                    Publish(false, null, null, first);
                    first = false;
                }
            }
        }

        void Evaluate(SCARD_READERSTATE[] states, int readerCount, ref bool first)
        {
            for (int i = 0; i < readerCount; i++)
            {
                uint s = states[i].dwEventState;
                if ((s & SCARD_STATE_PRESENT) != 0 && (s & SCARD_STATE_MUTE) == 0)
                {
                    string atr = ToHex(states[i].rgbAtr, (int)Math.Min(states[i].cbAtr, 36));
                    Publish(true, states[i].szReader, atr, first);
                    first = false;
                    return;
                }
            }
            Publish(false, null, null, first);
            first = false;
        }

        void Publish(bool present, string reader, string atr, bool force)
        {
            bool changed = present != cardPresent || (present && atr != currentAtr);
            cardPresent = present;
            currentReader = reader;
            currentAtr = atr;
            if ((changed || force) && CardChanged != null)
                CardChanged(present, reader, atr);
        }

        static List<string> ListReaders(IntPtr ctx)
        {
            List<string> result = new List<string>();
            int len = 0;
            int rc = SCardListReadersW(ctx, null, null, ref len);
            if (rc == SCARD_E_NO_READERS_AVAILABLE || rc != SCARD_S_SUCCESS || len == 0)
                return result;
            char[] buf = new char[len];
            if (SCardListReadersW(ctx, null, buf, ref len) != SCARD_S_SUCCESS)
                return result;
            foreach (string r in new string(buf, 0, len).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(r);
            return result;
        }

        static string ToHex(byte[] b, int n)
        {
            StringBuilder sb = new StringBuilder(n * 2);
            for (int i = 0; i < n; i++) sb.Append(b[i].ToString("X2"));
            return sb.ToString();
        }
    }
}
