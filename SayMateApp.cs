using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal sealed class WordEntry
{
    public string word { get; set; }
    public string pronunciation { get; set; }
    public string chinese { get; set; }
    public string added { get; set; }
}

internal sealed class SpeechSettings
{
    public string region { get; set; }
    public string encryptedKey { get; set; }
}

internal sealed class TimeoutWebClient : WebClient
{
    protected override WebRequest GetWebRequest(Uri address)
    {
        WebRequest request = base.GetWebRequest(address);
        request.Timeout = 5000;
        return request;
    }
}

internal sealed class SayMateContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly string dataFolder;
    private readonly string dataFile;
    private readonly string settingsFile;
    private bool leftWasDown;
    private bool paused;
    private bool capturing;
    private string lastWord = "";
    private DateTime lastShown = DateTime.MinValue;

    public SayMateContext()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dataFolder = Path.Combine(localData, "SayMate");
        dataFile = Path.Combine(dataFolder, "vocabulary.json");
        settingsFile = Path.Combine(dataFolder, "settings.json");
        Directory.CreateDirectory(dataFolder);
        MigrateOldData(Path.Combine(localData, "EnPronounce"));

        var menu = new ContextMenuStrip();
        menu.Items.Add("Speech settings", null, delegate { ShowSpeechSettings(); });
        menu.Items.Add("Open vocabulary", null, delegate { ShowVocabulary(); });
        var pauseItem = menu.Items.Add("Pause selection detection");
        pauseItem.Click += delegate {
            paused = !paused;
            pauseItem.Text = paused ? "Resume selection detection" : "Pause selection detection";
        };
        menu.Items.Add("Exit", null, delegate { ExitThread(); });

        tray = new NotifyIcon {
            Icon = SystemIcons.Information,
            Text = "SayMate",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += delegate { ShowVocabulary(); };

        timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += OnTimerTick;
        timer.Start();
        tray.ShowBalloonTip(3000, "SayMate is running",
            "Select one English word to translate, pronounce, or save it.", ToolTipIcon.Info);
    }

    private void OnTimerTick(object sender, EventArgs e)
    {
        bool down = (Control.MouseButtons & MouseButtons.Left) != 0;
        if (leftWasDown && !down && !paused && !capturing)
            CaptureSelection();
        leftWasDown = down;
    }

    private void CaptureSelection()
    {
        capturing = true;
        try {
            Thread.Sleep(120);
            string selected = ReadAccessibilitySelection();
            if (!IsEnglishWord(selected)) selected = CopySelection();
            if (IsEnglishWord(selected)) ShowPopup(selected.Trim());
        }
        catch { }
        finally { capturing = false; }
    }

    private static string ReadAccessibilitySelection()
    {
        try {
            AutomationElement element = AutomationElement.FocusedElement;
            while (element != null) {
                object value;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out value)) {
                    System.Windows.Automation.Text.TextPatternRange[] ranges = ((TextPattern)value).GetSelection();
                    if (ranges != null && ranges.Length > 0) {
                        string text = ranges[0].GetText(100).Trim();
                        if (text.Length > 0) return text;
                    }
                }
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
        } catch { }
        return "";
    }

    private static string CopySelection()
    {
        string previous = null;
        bool hadText = false;
        try {
            hadText = Clipboard.ContainsText();
            if (hadText) previous = Clipboard.GetText();
            Clipboard.Clear();
            SendKeys.SendWait("^c");
            Thread.Sleep(140);
            string selected = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (hadText) Clipboard.SetText(previous); else Clipboard.Clear();
            return selected;
        } catch { return ""; }
    }

    private static bool IsEnglishWord(string text)
    {
        if (String.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
        string value = text.Trim();
        for (int i = 0; i < value.Length; i++) {
            char c = value[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                  ((c == '-' || c == '\'') && i > 0 && i < value.Length - 1))) return false;
        }
        return true;
    }

    private void ShowPopup(string word)
    {
        if (String.Equals(word, lastWord, StringComparison.OrdinalIgnoreCase) &&
            (DateTime.Now - lastShown).TotalSeconds < 2) return;
        lastWord = word;
        lastShown = DateTime.Now;

        var popup = new Form {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.White,
            ClientSize = new Size(270, 62),
            Padding = new Padding(8)
        };
        popup.Paint += delegate(object s, PaintEventArgs e) {
            using (var pen = new Pen(Color.FromArgb(210, 215, 225)))
                e.Graphics.DrawRectangle(pen, 0, 0, popup.ClientSize.Width - 1, popup.ClientSize.Height - 1);
        };
        string phonetic = "";
        string chinese = "";
        var label = new Label { Text = "翻译中…", AutoEllipsis = true, Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold), Location = new Point(12, 17), Size = new Size(165, 30) };
        var play = new Button { Text = "▶", Font = new Font("Segoe UI Symbol", 12), Location = new Point(180, 11), Size = new Size(36, 39), FlatStyle = FlatStyle.System };
        var add = new Button { Text = "+", Font = new Font("Segoe UI", 15, FontStyle.Bold), Location = new Point(222, 11), Size = new Size(36, 39), FlatStyle = FlatStyle.System };
        play.Click += delegate { Speak(word); };
        add.Click += delegate {
            add.Enabled = false; add.Text = "…";
            if (String.IsNullOrWhiteSpace(chinese)) GetDetails(word, out phonetic, out chinese);
            bool saved = SaveWord(word, phonetic, chinese);
            add.Text = "✓";
            new ToolTip().SetToolTip(add, saved ? "Saved: " + phonetic + " " + chinese : "Already saved");
        };
        popup.Controls.AddRange(new Control[] { label, play, add });
        Point cursor = Cursor.Position;
        Screen screen = Screen.FromPoint(cursor);
        int x = Math.Min(cursor.X + 14, screen.WorkingArea.Right - popup.Width);
        int y = Math.Min(cursor.Y + 14, screen.WorkingArea.Bottom - popup.Height);
        popup.Location = new Point(Math.Max(screen.WorkingArea.Left, x), Math.Max(screen.WorkingArea.Top, y));
        popup.Deactivate += delegate { popup.Close(); };
        var closeTimer = new System.Windows.Forms.Timer { Interval = 12000 };
        closeTimer.Tick += delegate { closeTimer.Stop(); popup.Close(); closeTimer.Dispose(); };
        popup.FormClosed += delegate { closeTimer.Dispose(); };
        closeTimer.Start();
        popup.Show();
        Thread translator = new Thread(new ThreadStart(delegate {
            GetDetails(word, out phonetic, out chinese);
            if (!popup.IsDisposed && popup.IsHandleCreated) {
                popup.BeginInvoke((MethodInvoker)delegate {
                    label.Text = String.IsNullOrWhiteSpace(chinese) ? "翻译暂时不可用" : chinese;
                    new ToolTip().SetToolTip(label, word + (String.IsNullOrWhiteSpace(phonetic) ? "" : "  " + phonetic));
                });
            }
        }));
        translator.IsBackground = true;
        translator.Start();
    }

    private void Speak(string word)
    {
        SpeechSettings settings = LoadSpeechSettings();
        string key = Unprotect(settings.encryptedKey);
        if (!String.IsNullOrWhiteSpace(key) && !String.IsNullOrWhiteSpace(settings.region)) {
            Thread worker = new Thread(new ThreadStart(delegate {
                if (!SpeakWithAzure(word, key, settings.region)) SpeakLocally(word);
            }));
            worker.IsBackground = true;
            worker.Start();
        } else SpeakLocally(word);
    }

    private static bool SpeakWithAzure(string word, string key, string region)
    {
        try {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string endpoint = "https://" + region.Trim() + ".tts.speech.microsoft.com/cognitiveservices/v1";
            string safeWord = System.Security.SecurityElement.Escape(word);
            string ssml = "<speak version='1.0' xml:lang='en-AU'><voice name='en-AU-NatashaNeural'>" + safeWord + "</voice></speak>";
            using (var web = new WebClient()) {
                web.Headers["Ocp-Apim-Subscription-Key"] = key.Trim();
                web.Headers["Content-Type"] = "application/ssml+xml";
                web.Headers["X-Microsoft-OutputFormat"] = "riff-24khz-16bit-mono-pcm";
                web.Headers["User-Agent"] = "SayMate";
                byte[] audio = web.UploadData(endpoint, "POST", Encoding.UTF8.GetBytes(ssml));
                using (var stream = new MemoryStream(audio))
                using (var player = new System.Media.SoundPlayer(stream)) {
                    player.PlaySync();
                }
            }
            return true;
        } catch { return false; }
    }

    private static void SpeakLocally(string word)
    {
        try {
            using (var speech = new SpeechSynthesizer()) {
                InstalledVoice au = speech.GetInstalledVoices().FirstOrDefault(v =>
                    v.Enabled && (v.VoiceInfo.Culture.Name.Equals("en-AU", StringComparison.OrdinalIgnoreCase) ||
                    v.VoiceInfo.Name.IndexOf("Australia", StringComparison.OrdinalIgnoreCase) >= 0));
                if (au != null) speech.SelectVoice(au.VoiceInfo.Name);
                speech.Rate = -1;
                speech.Speak(word);
            }
        } catch { System.Media.SystemSounds.Exclamation.Play(); }
    }

    private SpeechSettings LoadSpeechSettings()
    {
        try {
            if (File.Exists(settingsFile)) {
                SpeechSettings value = new JavaScriptSerializer().Deserialize<SpeechSettings>(File.ReadAllText(settingsFile));
                if (value != null) return value;
            }
        } catch { }
        return new SpeechSettings { region = "australiaeast", encryptedKey = "" };
    }

    private static string Protect(string value)
    {
        if (String.IsNullOrEmpty(value)) return "";
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string value)
    {
        try {
            if (String.IsNullOrEmpty(value)) return "";
            byte[] clear = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        } catch { return ""; }
    }

    private void ShowSpeechSettings()
    {
        SpeechSettings current = LoadSpeechSettings();
        var form = new Form { Text = "SayMate - Natasha voice", Width = 470, Height = 235, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var voiceLabel = new Label { Text = "Voice: en-AU-NatashaNeural (female)", Location = new Point(20, 18), AutoSize = true };
        var regionLabel = new Label { Text = "Azure region", Location = new Point(20, 53), AutoSize = true };
        var regionBox = new TextBox { Text = String.IsNullOrWhiteSpace(current.region) ? "australiaeast" : current.region, Location = new Point(130, 49), Width = 300 };
        var keyLabel = new Label { Text = "Speech API key", Location = new Point(20, 88), AutoSize = true };
        var keyBox = new TextBox { Text = Unprotect(current.encryptedKey), Location = new Point(130, 84), Width = 300, UseSystemPasswordChar = true };
        var note = new Label { Text = "The key is encrypted for your Windows account and stored only on this PC.", Location = new Point(20, 122), Size = new Size(415, 35), ForeColor = Color.DimGray };
        var save = new Button { Text = "Save", Location = new Point(350, 155), Width = 80, DialogResult = DialogResult.OK };
        form.Controls.AddRange(new Control[] { voiceLabel, regionLabel, regionBox, keyLabel, keyBox, note, save });
        form.AcceptButton = save;
        if (form.ShowDialog() == DialogResult.OK) {
            var settings = new SpeechSettings { region = regionBox.Text.Trim(), encryptedKey = Protect(keyBox.Text.Trim()) };
            File.WriteAllText(settingsFile, new JavaScriptSerializer().Serialize(settings));
            MessageBox.Show("Natasha is now the preferred voice. Select a word, then press ▶.", "SayMate", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ShowVocabulary()
    {
        var form = new Form { Text = "SayMate - My words", Width = 700, Height = 430, StartPosition = FormStartPosition.CenterScreen };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, DataSource = LoadWords() };
        form.Controls.Add(grid);
        form.Show();
    }

    private List<WordEntry> LoadWords()
    {
        try {
            if (!File.Exists(dataFile)) return new List<WordEntry>();
            return new JavaScriptSerializer().Deserialize<List<WordEntry>>(File.ReadAllText(dataFile)) ?? new List<WordEntry>();
        } catch { return new List<WordEntry>(); }
    }

    private bool SaveWord(string word, string pronunciation, string chinese)
    {
        List<WordEntry> words = LoadWords();
        WordEntry existing = words.FirstOrDefault(w => String.Equals(w.word, word, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            if (!String.IsNullOrWhiteSpace(pronunciation)) existing.pronunciation = pronunciation;
            if (!String.IsNullOrWhiteSpace(chinese)) existing.chinese = chinese;
            File.WriteAllText(dataFile, new JavaScriptSerializer().Serialize(words));
            return false;
        }
        words.Add(new WordEntry { word = word, pronunciation = pronunciation, chinese = chinese, added = DateTime.Now.ToString("s") });
        File.WriteAllText(dataFile, new JavaScriptSerializer().Serialize(words));
        return true;
    }

    private static void GetDetails(string word, out string phonetic, out string chinese)
    {
        phonetic = ""; chinese = "";
        using (var web = new TimeoutWebClient()) {
            web.Encoding = System.Text.Encoding.UTF8;
            try {
                object root = new JavaScriptSerializer().DeserializeObject(web.DownloadString("https://api.dictionaryapi.dev/api/v2/entries/en/" + Uri.EscapeDataString(word)));
                var entries = root as object[];
                if (entries != null && entries.Length > 0) {
                    var entry = entries[0] as Dictionary<string, object>;
                    if (entry != null && entry.ContainsKey("phonetic")) phonetic = Convert.ToString(entry["phonetic"]);
                    if (String.IsNullOrWhiteSpace(phonetic) && entry != null && entry.ContainsKey("phonetics")) {
                        var phonetics = entry["phonetics"] as object[];
                        if (phonetics != null) {
                            foreach (object item in phonetics) {
                                var candidate = item as Dictionary<string, object>;
                                if (candidate != null && candidate.ContainsKey("text")) {
                                    string text = Convert.ToString(candidate["text"]);
                                    if (!String.IsNullOrWhiteSpace(text)) { phonetic = text; break; }
                                }
                            }
                        }
                    }
                }
            } catch { }
            if (String.IsNullOrWhiteSpace(phonetic)) {
                try {
                    web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130 Safari/537.36";
                    string url = "https://dictionary.cambridge.org/dictionary/english/" + Uri.EscapeDataString(word.ToLowerInvariant());
                    string html = System.Web.HttpUtility.HtmlDecode(web.DownloadString(url));
                    Match match = Regex.Match(html, @"<span[^>]*class=""[^""]*\bipa\b[^""]*""[^>]*>(.*?)</span>", RegexOptions.Singleline);
                    if (match.Success) {
                        string value = Regex.Replace(match.Groups[1].Value, "<[^>]+>", "").Trim().Trim('/');
                        if (!String.IsNullOrWhiteSpace(value)) phonetic = "/" + value + "/";
                    }
                    web.Headers.Remove(HttpRequestHeader.UserAgent);
                } catch { }
            }
            try {
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q=" + Uri.EscapeDataString(word);
                object root = new JavaScriptSerializer().DeserializeObject(web.DownloadString(url));
                var result = root as object[];
                var blocks = result != null && result.Length > 0 ? result[0] as object[] : null;
                var first = blocks != null && blocks.Length > 0 ? blocks[0] as object[] : null;
                if (first != null && first.Length > 0) chinese = Convert.ToString(first[0]);
            } catch { }
            if (String.IsNullOrWhiteSpace(chinese)) {
                try {
                    object root = new JavaScriptSerializer().DeserializeObject(web.DownloadString("https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(word) + "&langpair=en|zh-CN"));
                    var dict = root as Dictionary<string, object>;
                    var response = dict == null ? null : dict["responseData"] as Dictionary<string, object>;
                    if (response != null) chinese = Convert.ToString(response["translatedText"]);
                } catch { }
            }
        }
    }

    private void MigrateOldData(string oldFolder)
    {
        try {
            if (!Directory.Exists(oldFolder)) return;
            string oldWords = Path.Combine(oldFolder, "vocabulary.json");
            string oldSettings = Path.Combine(oldFolder, "settings.json");
            if (!File.Exists(dataFile) && File.Exists(oldWords)) File.Copy(oldWords, dataFile);
            if (!File.Exists(settingsFile) && File.Exists(oldSettings)) File.Copy(oldSettings, settingsFile);
        } catch { }
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SayMateContext());
    }
}
