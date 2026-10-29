using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YouTubeLite
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
        private static Mutex mutex = null;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            mutex = new Mutex(true, "YouTubeLite_SingleInstance_App", out createdNew);

            if (!createdNew)
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"), ex.ToString());
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }
    }

    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private Panel titleBarPanel;
        private Label lblAppTitle;
        private Label lblVideoTitle;
        private Label btnBack;
        private Label btnForward;
        private Label btnReload;
        private Label btnDownload;
        private Label btnMinimize;
        private Label btnMaximize;
        private Label btnClose;
        private bool isDownloading = false;
        private Process activeDownloadProcess = null;

        private WebView2 webView;
        private FormWindowState previousWindowState = FormWindowState.Maximized;
        private Rectangle previousBounds;
        private bool isFullScreen = false;

        public MainForm()
        {
            this.Text = "YouTube Lite";
            this.Size = new Size(1280, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(8, 8, 10);
            this.WindowState = FormWindowState.Maximized;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "youtube.ico");
            if (File.Exists(iconPath))
            {
                try { this.Icon = new Icon(iconPath); } catch { }
            }

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = Color.FromArgb(8, 8, 10);
            this.Controls.Add(webView);

            SetupCustomTitleBar();
            titleBarPanel.SendToBack();

            InitializeWebView();
        }

        private void SetupCustomTitleBar()
        {
            titleBarPanel = new Panel();
            titleBarPanel.Height = 36;
            titleBarPanel.Dock = DockStyle.Top;
            titleBarPanel.BackColor = Color.FromArgb(10, 11, 16);
            titleBarPanel.Padding = new Padding(8, 0, 0, 0);

            // Dragging window on title bar
            titleBarPanel.MouseDown += TitleBar_MouseDown;
            titleBarPanel.DoubleClick += (s, e) => ToggleMaximize();

            // 1. App Logo / Dot + Label
            lblAppTitle = new Label();
            lblAppTitle.Text = "▶ YouTube Lite";
            lblAppTitle.ForeColor = Color.FromArgb(240, 242, 245);
            lblAppTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblAppTitle.AutoSize = true;
            lblAppTitle.Location = new Point(10, 8);
            lblAppTitle.MouseDown += TitleBar_MouseDown;
            lblAppTitle.DoubleClick += (s, e) => ToggleMaximize();
            titleBarPanel.Controls.Add(lblAppTitle);

            // 2. Navigation Buttons (Back, Forward, Reload)
            btnBack = CreateTitleBarButton("‹", 26, 26, 125, 5, 12f);
            btnBack.Click += (s, e) => {
                if (webView != null && webView.CoreWebView2 != null && webView.CoreWebView2.CanGoBack)
                    webView.CoreWebView2.GoBack();
            };
            titleBarPanel.Controls.Add(btnBack);

            btnForward = CreateTitleBarButton("›", 26, 26, 155, 5, 12f);
            btnForward.Click += (s, e) => {
                if (webView != null && webView.CoreWebView2 != null && webView.CoreWebView2.CanGoForward)
                    webView.CoreWebView2.GoForward();
            };
            titleBarPanel.Controls.Add(btnForward);

            btnReload = CreateTitleBarButton("⟳", 26, 26, 185, 5, 10.5f);
            btnReload.Click += (s, e) => {
                if (webView != null && webView.CoreWebView2 != null)
                    webView.CoreWebView2.Reload();
            };
            titleBarPanel.Controls.Add(btnReload);

            btnDownload = CreateTitleBarButton("⬇", 26, 26, 215, 5, 10.5f);
            btnDownload.Click += (s, e) => StartDownload();
            ToolTip tipDownload = new ToolTip();
            tipDownload.SetToolTip(btnDownload, "Download Video 1080p MP4");
            titleBarPanel.Controls.Add(btnDownload);

            // 3. Center Video Title
            lblVideoTitle = new Label();
            lblVideoTitle.Text = "Memuat...";
            lblVideoTitle.ForeColor = Color.FromArgb(145, 150, 165);
            lblVideoTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblVideoTitle.AutoEllipsis = true;
            lblVideoTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblVideoTitle.Location = new Point(248, 7);
            lblVideoTitle.Size = new Size(500, 22);
            lblVideoTitle.MouseDown += TitleBar_MouseDown;
            lblVideoTitle.DoubleClick += (s, e) => ToggleMaximize();
            titleBarPanel.Controls.Add(lblVideoTitle);

            // 4. Window Action Controls (Minimize, Maximize, Close in standard Windows order)
            btnMinimize = new Label();
            btnMinimize.Text = "─";
            btnMinimize.Size = new Size(46, 36);
            btnMinimize.Dock = DockStyle.Right;
            btnMinimize.TextAlign = ContentAlignment.MiddleCenter;
            btnMinimize.ForeColor = Color.FromArgb(160, 165, 180);
            btnMinimize.Font = new Font("Segoe UI", 8f);
            btnMinimize.Cursor = Cursors.Hand;
            btnMinimize.MouseEnter += (s, e) => { btnMinimize.BackColor = Color.FromArgb(30, 32, 45); btnMinimize.ForeColor = Color.White; };
            btnMinimize.MouseLeave += (s, e) => { btnMinimize.BackColor = Color.Transparent; btnMinimize.ForeColor = Color.FromArgb(160, 165, 180); };
            btnMinimize.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            titleBarPanel.Controls.Add(btnMinimize);

            btnMaximize = new Label();
            btnMaximize.Text = (this.WindowState == FormWindowState.Maximized) ? "❐" : "□";
            btnMaximize.Size = new Size(46, 36);
            btnMaximize.Dock = DockStyle.Right;
            btnMaximize.TextAlign = ContentAlignment.MiddleCenter;
            btnMaximize.ForeColor = Color.FromArgb(160, 165, 180);
            btnMaximize.Font = new Font("Segoe UI", 10f);
            btnMaximize.Cursor = Cursors.Hand;
            btnMaximize.MouseEnter += (s, e) => { btnMaximize.BackColor = Color.FromArgb(30, 32, 45); btnMaximize.ForeColor = Color.White; };
            btnMaximize.MouseLeave += (s, e) => { btnMaximize.BackColor = Color.Transparent; btnMaximize.ForeColor = Color.FromArgb(160, 165, 180); };
            btnMaximize.Click += (s, e) => ToggleMaximize();
            titleBarPanel.Controls.Add(btnMaximize);

            btnClose = new Label();
            btnClose.Text = "✕";
            btnClose.Size = new Size(46, 36);
            btnClose.Dock = DockStyle.Right;
            btnClose.TextAlign = ContentAlignment.MiddleCenter;
            btnClose.ForeColor = Color.FromArgb(160, 165, 180);
            btnClose.Font = new Font("Segoe UI", 9f);
            btnClose.Cursor = Cursors.Hand;
            btnClose.MouseEnter += (s, e) => { btnClose.BackColor = Color.FromArgb(225, 29, 72); btnClose.ForeColor = Color.White; };
            btnClose.MouseLeave += (s, e) => { btnClose.BackColor = Color.Transparent; btnClose.ForeColor = Color.FromArgb(160, 165, 180); };
            btnClose.Click += (s, e) => this.Close();
            titleBarPanel.Controls.Add(btnClose);

            this.Controls.Add(titleBarPanel);
            titleBarPanel.SendToBack();

            // Responsive adjustment for video title width
            this.Resize += (s, e) => {
                if (btnMaximize != null)
                {
                    btnMaximize.Text = (this.WindowState == FormWindowState.Maximized) ? "❐" : "□";
                }
                int available = this.ClientSize.Width - 248 - 160;
                if (available > 100)
                {
                    lblVideoTitle.Width = available;
                }
            };
        }

        private Label CreateTitleBarButton(string text, int width, int height, int x, int y, float fontSize)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Size = new Size(width, height);
            lbl.Location = new Point(x, y);
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.ForeColor = Color.FromArgb(160, 165, 180);
            lbl.Font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            lbl.Cursor = Cursors.Hand;
            lbl.MouseEnter += (s, e) => { lbl.BackColor = Color.FromArgb(25, 28, 38); lbl.ForeColor = Color.White; };
            lbl.MouseLeave += (s, e) => { lbl.BackColor = Color.Transparent; lbl.ForeColor = Color.FromArgb(160, 165, 180); };
            return lbl;
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void ToggleMaximize()
        {
            if (this.WindowState == FormWindowState.Maximized)
            {
                this.WindowState = FormWindowState.Normal;
                btnMaximize.Text = "□";
            }
            else
            {
                this.WindowState = FormWindowState.Maximized;
                btnMaximize.Text = "❐";
            }
        }

        // Resizable borders for borderless Form
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (this.WindowState == FormWindowState.Normal && (int)m.Result == HTCLIENT)
                {
                    int x = unchecked((short)(m.LParam.ToInt64() & 0xFFFF));
                    int y = unchecked((short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                    Point pt = this.PointToClient(new Point(x, y));
                    int borderSize = 6;

                    if (pt.X <= borderSize && pt.Y <= borderSize) m.Result = (IntPtr)HTTOPLEFT;
                    else if (pt.X >= this.ClientSize.Width - borderSize && pt.Y <= borderSize) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (pt.X <= borderSize && pt.Y >= this.ClientSize.Height - borderSize) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (pt.X >= this.ClientSize.Width - borderSize && pt.Y >= this.ClientSize.Height - borderSize) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (pt.X <= borderSize) m.Result = (IntPtr)HTLEFT;
                    else if (pt.X >= this.ClientSize.Width - borderSize) m.Result = (IntPtr)HTRIGHT;
                    else if (pt.Y <= borderSize) m.Result = (IntPtr)HTTOP;
                    else if (pt.Y >= this.ClientSize.Height - borderSize) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        private async void InitializeWebView()
        {
            try
            {
                string dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YouTubeLiteApp"
                );

                var options = new CoreWebView2EnvironmentOptions(
                    "--enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist --enable-hardware-overlays --disable-features=UseEcoQoSForBackgroundProcess"
                );

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
                await webView.EnsureCoreWebView2Async(env);

                // Set PREF cookie for YouTube Dark Mode so YouTube renders dark natively
                try
                {
                    var cookie = webView.CoreWebView2.CookieManager.CreateCookie("PREF", "f6=400", ".youtube.com", "/");
                    webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                }
                catch (Exception) { }

                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                // Sync Document Title to Titlebar (Clean up notification counts like '(1654)')
                webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    if (isDownloading) return;
                    string docTitle = webView.CoreWebView2.DocumentTitle;
                    if (!string.IsNullOrEmpty(docTitle))
                    {
                        string cleanTitle = docTitle.Replace(" - YouTube", "").Trim();
                        cleanTitle = Regex.Replace(cleanTitle, @"^\(\d+\)\s*", "");
                        if (string.Equals(cleanTitle, "YouTube", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(cleanTitle))
                        {
                            lblVideoTitle.Text = "";
                            this.Text = "YouTube Lite";
                        }
                        else
                        {
                            lblVideoTitle.Text = cleanTitle;
                            this.Text = cleanTitle + " - YouTube Lite";
                        }
                    }
                };

                // Handle Fullscreen
                webView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
                {
                    if (webView.CoreWebView2.ContainsFullScreenElement)
                    {
                        EnterFullScreen();
                    }
                    else
                    {
                        ExitFullScreen();
                    }
                };

                // Keyboard shortcuts (F11 toggle fullscreen, Esc exit)
                this.KeyPreview = true;
                KeyEventHandler handleKeys = (s, e) =>
                {
                    if (e.KeyCode == Keys.F11)
                    {
                        e.Handled = true;
                        ToggleFullScreen();
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        if (isFullScreen && (webView.CoreWebView2 == null || !webView.CoreWebView2.ContainsFullScreenElement))
                        {
                            e.Handled = true;
                            ToggleFullScreen();
                        }
                    }
                };
                this.KeyDown += handleKeys;
                webView.KeyDown += handleKeys;

                // 1. NATIVE BROWSER EXTENSION (uBlock Origin Lite)
                string ubolPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "extensions", "ubolite"));
                if (Directory.Exists(ubolPath))
                {
                    try
                    {
                        var existing = await webView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                        bool alreadyInstalled = false;
                        foreach (var ext in existing)
                        {
                            if (ext.IsEnabled)
                            {
                                alreadyInstalled = true;
                                break;
                            }
                        }
                        if (!alreadyInstalled)
                        {
                            await webView.CoreWebView2.Profile.AddBrowserExtensionAsync(ubolPath);
                        }
                    }
                    catch (Exception) { }
                }

                // 2. NETWORK BLOCKING
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += (s, e) =>
                {
                    string uri = e.Request.Uri.ToLowerInvariant();
                    if (uri.Contains("googlesyndication.com") ||
                        uri.Contains("doubleclick.net") ||
                        uri.Contains("/pagead/") ||
                        uri.Contains("/api/stats/ads") ||
                        uri.Contains("/ptracking") ||
                        uri.Contains("adservice.google") ||
                        uri.Contains("youtube.com/ads") ||
                        uri.Contains("play.google.com/log") ||
                        uri.Contains("youtube.com/get_midroll_info") ||
                        uri.Contains("youtubei/v1/att/get") ||
                        uri.Contains("gnjoy.hk"))
                    {
                        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            new MemoryStream(new byte[0]),
                            403,
                            "Blocked",
                            "Content-Type: text/plain"
                        );
                    }
                };

                // 3. BRAVE PRUNING + OBSIDIAN ALIVE THEME + ZERO ADS
                string jsCode = @"
(function() {
    'use strict';

    // A. Hardware Acceleration (H.264 Priority)
    try {
        const origCanPlay = HTMLMediaElement.prototype.canPlayType;
        HTMLMediaElement.prototype.canPlayType = function(type) {
            if (type && (type.includes('av01') || type.includes('vp09') || type.includes('vp9'))) return '';
            return origCanPlay.apply(this, arguments);
        };
        if (window.MediaSource && MediaSource.isTypeSupported) {
            const origIsSupported = MediaSource.isTypeSupported;
            MediaSource.isTypeSupported = function(type) {
                if (type && (type.includes('av01') || type.includes('vp09') || type.includes('vp9'))) return false;
                return origIsSupported.apply(this, arguments);
            };
        }
    } catch(e) {}

    // B. Ensure Clean Obsidian & Editorial Style is ALWAYS active
    function ensureStyle() {
        try {
            document.documentElement.setAttribute('dark', 'true');
            const target = document.head || document.documentElement;
            if (target && !document.getElementById('yt-font-link')) {
                const l = document.createElement('link');
                l.id = 'yt-font-link';
                l.rel = 'stylesheet';
                l.href = 'https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@500;600;700;800&display=swap';
                target.appendChild(l);
            }
            if (target && !document.getElementById('yt-clean-style')) {
                const s = document.createElement('style');
                s.id = 'yt-clean-style';
                s.textContent = `
                    /* Fonts Direct @font-face */
                    @font-face {
                      font-family: 'Plus Jakarta Sans';
                      font-style: normal;
                      font-weight: 500;
                      font-display: swap;
                      src: url('https://fonts.gstatic.com/s/plusjakartasans/v12/LDIbaomQNQcsA88c7O9yZ4KMCoOg4IA6-91aHEjcWuA_m07NSg.ttf') format('truetype');
                    }
                    @font-face {
                      font-family: 'Plus Jakarta Sans';
                      font-style: normal;
                      font-weight: 600;
                      font-display: swap;
                      src: url('https://fonts.gstatic.com/s/plusjakartasans/v12/LDIbaomQNQcsA88c7O9yZ4KMCoOg4IA6-91aHEjcWuA_d0nNSg.ttf') format('truetype');
                    }
                    @font-face {
                      font-family: 'Plus Jakarta Sans';
                      font-style: normal;
                      font-weight: 700;
                      font-display: swap;
                      src: url('https://fonts.gstatic.com/s/plusjakartasans/v12/LDIbaomQNQcsA88c7O9yZ4KMCoOg4IA6-91aHEjcWuA_TknNSg.ttf') format('truetype');
                    }
                    @font-face {
                      font-family: 'Plus Jakarta Sans';
                      font-style: normal;
                      font-weight: 800;
                      font-display: swap;
                      src: url('https://fonts.gstatic.com/s/plusjakartasans/v12/LDIbaomQNQcsA88c7O9yZ4KMCoOg4IA6-91aHEjcWuA_KUnNSg.ttf') format('truetype');
                    }

                    /* Ad Block Defuser */
                    #masthead-ad,
                    ytd-ad-slot-renderer,
                    ytd-banner-promo-renderer,
                    ytd-rich-item-renderer:has(ytd-ad-slot-renderer),
                    ytd-rich-section-renderer:has(ytd-ad-slot-renderer),
                    ytd-rich-item-renderer:has(#ad-badge),
                    ytd-rich-item-renderer:has(.badge-shape-wiz--thumbnail-badge),
                    ytd-rich-item-renderer:has([aria-label*=""Bersponsor""]),
                    ytd-rich-item-renderer:has([aria-label*=""Sponsored""]),
                    #player-ads,
                    #companion,
                    ytd-companion-slot-renderer,
                    ytd-action-companion-ad-renderer,
                    ytd-promoted-sparkles-web-renderer,
                    ytd-engagement-panel-section-list-renderer[target-id=""engagement-panel-ads""],
                    .ytp-ad-module,
                    .ytp-ad-overlay-container,
                    .ytp-ad-message-container,
                    ytd-in-feed-ad-layout-renderer,
                    tp-yt-paper-dialog:has(ytd-enforcement-message-view-model),
                    ytd-popup-container:has(ytd-enforcement-message-view-model),
                    tp-yt-iron-overlay-backdrop {
                        display: none !important;
                    }

                    /* Hide Shorts Carousels from Feed */
                    ytd-rich-section-renderer:has(ytd-rich-shelf-renderer[is-shorts]),
                    ytd-reel-shelf-renderer,
                    ytd-rich-shelf-renderer[is-shorts] {
                        display: none !important;
                    }

                    /* Global YouTube Variables & Typography Override */
                    :root, html, html[dark], [dark], body, ytd-app {
                        --yt-font-family: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-primary: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-secondary: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-subheadline: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-title-1: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-title-2: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-title-3: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-headline: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-body-1: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-body-2: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        --yt-spec-font-caption: 'Plus Jakarta Sans', system-ui, sans-serif !important;

                        --yt-spec-base-background: #08080a !important;
                        --yt-spec-raised-background: #0e1017 !important;
                        --yt-spec-menu-background: #12131a !important;
                        --yt-spec-inverted-background: #f3f4f6 !important;
                        --yt-spec-general-background-a: #08080a !important;
                        --yt-spec-general-background-b: #08080a !important;
                        --yt-spec-text-primary: #f3f4f6 !important;
                        --yt-spec-text-secondary: #9ca3af !important;
                        --yt-spec-text-disabled: #6b7280 !important;
                        --yt-spec-icon-active-other: #f3f4f6 !important;
                        --yt-spec-icon-inactive: #9ca3af !important;
                        --yt-spec-icon-disabled: #4b5563 !important;
                        --yt-spec-badge-chip-background: #141520 !important;
                        --yt-spec-button-chip-background-hover: #1e202e !important;
                        --ytd-searchbox-background: #12131a !important;
                        --ytd-searchbox-text-color: #f3f4f6 !important;
                    }

                    /* Base Backgrounds - Obsidian OLED (#08080a) */
                    html, html[dark], [dark], body, ytd-app, #page-manager, #content,
                    ytd-masthead, #masthead, #background.ytd-masthead,
                    #chips-wrapper.ytd-feed-filter-chip-bar-renderer, ytd-feed-filter-chip-bar-renderer {
                        background-color: #08080a !important;
                        background: #08080a !important;
                    }

                    /* Sidebar Navigation & Icons */
                    #guide-content.ytd-app, ytd-guide-renderer, #guide-inner-content.ytd-app {
                        background-color: #08080a !important;
                        border-right: 1px solid #161822 !important;
                    }
                    ytd-guide-entry-renderer yt-formatted-string,
                    #guide-section-title.ytd-guide-section-renderer,
                    ytd-guide-entry-renderer yt-icon,
                    .guide-entry-badge.ytd-guide-entry-renderer {
                        color: #9ca3af !important;
                        fill: #9ca3af !important;
                    }
                    ytd-guide-entry-renderer[active] yt-formatted-string,
                    ytd-guide-entry-renderer:hover yt-formatted-string {
                        color: #ffffff !important;
                    }
                    ytd-guide-entry-renderer[active] yt-icon,
                    ytd-guide-entry-renderer:hover yt-icon {
                        color: #ffffff !important;
                        fill: #ffffff !important;
                    }
                    ytd-guide-entry-renderer:hover,
                    tp-yt-paper-item:hover {
                        background-color: #141620 !important;
                        border-radius: 8px !important;
                    }

                    /* Search Bar - Sleek Obsidian Pill */
                    #container.ytd-searchbox,
                    form.ytd-searchbox,
                    ytd-searchbox[desktop-search-redesign] #container.ytd-searchbox {
                        background-color: #12131a !important;
                        border: 1px solid #222533 !important;
                        box-shadow: none !important;
                        border-radius: 9999px !important;
                    }
                    #container.ytd-searchbox input,
                    #search-input input,
                    input#search {
                        background: transparent !important;
                        background-color: transparent !important;
                        color: #f3f4f6 !important;
                    }
                    #container.ytd-searchbox input::placeholder,
                    input#search::placeholder {
                        color: #6b7280 !important;
                    }
                    #search-icon-legacy.ytd-searchbox,
                    ytd-searchbox[desktop-search-redesign] #search-icon-legacy.ytd-searchbox {
                        background-color: #161824 !important;
                        border: 1px solid #222533 !important;
                        border-left: none !important;
                        border-radius: 0 9999px 9999px 0 !important;
                    }
                    #search-icon-legacy.ytd-searchbox yt-icon,
                    #search-icon-legacy.ytd-searchbox yt-icon svg {
                        color: #9ca3af !important;
                        fill: #9ca3af !important;
                    }
                    #voice-search-button.ytd-masthead yt-icon-button,
                    #voice-search-button.ytd-masthead button {
                        background-color: #141620 !important;
                        border-radius: 50% !important;
                    }
                    #voice-search-button.ytd-masthead yt-icon {
                        color: #f3f4f6 !important;
                        fill: #f3f4f6 !important;
                    }

                    /* Filter Chips */
                    yt-chip-cloud-chip-renderer[chip-style=""STYLE_DEFAULT""],
                    yt-chip-cloud-chip-renderer {
                        background-color: #141520 !important;
                        border: 1px solid #202332 !important;
                        color: #d1d5db !important;
                        border-radius: 9999px !important;
                    }
                    yt-chip-cloud-chip-renderer yt-formatted-string {
                        color: #d1d5db !important;
                    }
                    yt-chip-cloud-chip-renderer:hover {
                        background-color: #1d2030 !important;
                    }
                    yt-chip-cloud-chip-renderer:hover yt-formatted-string {
                        color: #ffffff !important;
                    }
                    yt-chip-cloud-chip-renderer[selected] {
                        background-color: #e11d48 !important;
                        border-color: #e11d48 !important;
                    }
                    yt-chip-cloud-chip-renderer[selected] yt-formatted-string {
                        color: #ffffff !important;
                        font-weight: 700 !important;
                    }

                    /* Editorial Typography & Metadata (Modern Lockup + Classic) */
                    #video-title,
                    #video-title.ytd-rich-grid-media,
                    #video-title.ytd-video-renderer,
                    #video-title.ytd-compact-video-renderer,
                    #video-title.ytd-grid-video-renderer,
                    #video-title-link,
                    ytd-rich-item-renderer h3,
                    ytd-rich-item-renderer h3 a,
                    ytd-rich-item-renderer h3 span,
                    yt-lockup-view-model h3,
                    yt-lockup-view-model h3 a,
                    yt-lockup-view-model h3 span,
                    .yt-lockup-metadata-view-model-wiz__heading,
                    .yt-lockup-metadata-view-model-wiz__title,
                    .yt-lockup-metadata-view-model-wiz__heading a,
                    .yt-lockup-metadata-view-model-wiz__heading span,
                    ytd-rich-grid-media h3,
                    ytd-rich-grid-media h3 a,
                    ytd-rich-grid-media h3 span {
                        font-family: 'Plus Jakarta Sans', system-ui, -apple-system, sans-serif !important;
                        font-weight: 700 !important;
                        letter-spacing: -0.015em !important;
                        color: #f8fafc !important;
                        font-size: 15.5px !important;
                        line-height: 1.4 !important;
                    }
                    #channel-name,
                    #channel-name yt-formatted-string,
                    #channel-name a,
                    #metadata-line,
                    #metadata-line span,
                    ytd-video-meta-block span,
                    .yt-lockup-metadata-view-model-wiz__metadata,
                    .yt-lockup-metadata-view-model-wiz__metadata span {
                        font-family: 'Plus Jakarta Sans', system-ui, sans-serif !important;
                        font-size: 13px !important;
                        color: #9ca3af !important;
                        line-height: 1.35 !important;
                    }
                    #channel-name a:hover {
                        color: #f3f4f6 !important;
                    }
                    h1.ytd-watch-metadata,
                    h1.ytd-watch-metadata yt-formatted-string,
                    .ytd-video-primary-info-renderer h1 {
                        font-family: 'Plus Jakarta Sans', system-ui, -apple-system, sans-serif !important;
                        font-weight: 800 !important;
                        letter-spacing: -0.02em !important;
                        font-size: 20px !important;
                        line-height: 1.35 !important;
                        color: #ffffff !important;
                    }

                    /* Video Thumbnail Cards: Modern rounded 12px with subtle border & hover glow */
                    ytd-rich-item-renderer {
                        transition: transform 0.2s cubic-bezier(0.2, 0, 0, 1) !important;
                    }
                    ytd-rich-item-renderer:hover {
                        transform: translateY(-2px) !important;
                    }
                    ytd-thumbnail, ytd-thumbnail #thumbnail, ytd-thumbnail img {
                        border-radius: 12px !important;
                    }
                    ytd-thumbnail {
                        border: 1px solid rgba(255, 255, 255, 0.08) !important;
                        overflow: hidden !important;
                        transition: border-color 0.25s ease, box-shadow 0.25s ease !important;
                    }
                    ytd-rich-item-renderer:hover ytd-thumbnail {
                        border-color: rgba(225, 29, 72, 0.6) !important;
                        box-shadow: 0 8px 24px -4px rgba(225, 29, 72, 0.25) !important;
                    }

                    /* Watch Page Container & Description */
                    ytd-watch-flexy, #columns.ytd-watch-flexy {
                        background-color: #060608 !important;
                    }
                    ytd-watch-metadata, #description.ytd-watch-metadata {
                        background-color: #0c0d14 !important;
                        border: 1px solid #1a1c28 !important;
                        border-radius: 14px !important;
                    }
                    #description.ytd-watch-metadata yt-formatted-string {
                        color: #d1d5db !important;
                    }

                    /* Hide Promotional Clutter & Popups */
                    ytd-banner-promo-renderer,
                    #big-yoodle,
                    ytd-statement-banner-renderer,
                    ytd-primetime-promo-renderer,
                    ytd-mealbar-promo-renderer,
                    ytd-clarification-renderer {
                        display: none !important;
                    }
                `;
                target.appendChild(s);
            }
        } catch(e) {}
    }
    ensureStyle();
    document.addEventListener('DOMContentLoaded', ensureStyle);

    // C. Brave-Style JSON Pruning Defuser
    function prunePlayerObject(obj) {
        if (!obj || typeof obj !== 'object') return obj;
        try {
            if (Array.isArray(obj)) {
                for (let i = 0; i < obj.length; i++) prunePlayerObject(obj[i]);
                return obj;
            }
            if (obj.playerResponse) prunePlayerObject(obj.playerResponse);
            if ('adPlacements' in obj) delete obj.adPlacements;
            if ('playerAds' in obj) delete obj.playerAds;
            if ('adSlots' in obj) delete obj.adSlots;
            if ('adBreakHeartbeatParams' in obj) delete obj.adBreakHeartbeatParams;
            if (obj.playerConfig && obj.playerConfig.adPlacementConfig) {
                delete obj.playerConfig.adPlacementConfig;
            }
            // Keep obj.playbackTracking intact so YouTube Watch History & watchtime progress work
        } catch(e) {}
        return obj;
    }

    let _ytInitialPlayerResponse = window.ytInitialPlayerResponse;
    Object.defineProperty(window, 'ytInitialPlayerResponse', {
        get() { return _ytInitialPlayerResponse; },
        set(val) { _ytInitialPlayerResponse = prunePlayerObject(val); },
        configurable: true,
        enumerable: true
    });

    let _ytplayer = window.ytplayer;
    Object.defineProperty(window, 'ytplayer', {
        get() { return _ytplayer; },
        set(val) {
            if (val && val.config && val.config.args && val.config.args.raw_player_response) {
                prunePlayerObject(val.config.args.raw_player_response);
            }
            _ytplayer = val;
        },
        configurable: true,
        enumerable: true
    });

    const origJSONParse = JSON.parse;
    JSON.parse = function(text, reviver) {
        const data = origJSONParse.apply(this, arguments);
        if (data && typeof data === 'object') {
            prunePlayerObject(data);
        }
        return data;
    };

    if (window.Response && Response.prototype.json) {
        const origResponseJson = Response.prototype.json;
        Response.prototype.json = async function() {
            const data = await origResponseJson.apply(this, arguments);
            if (data && typeof data === 'object') {
                prunePlayerObject(data);
            }
            return data;
        };
    }

    // D. Active Watchdog & Instant Ad Annihilator
    function cleanWatchdog() {
        try {
            ensureStyle();

            const player = document.getElementById('movie_player');
            if (player && typeof player.skipAd === 'function') {
                player.skipAd();
            }

            const skipBtns = document.querySelectorAll(
                '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, .videoAdUiSkipButton, button.ytp-ad-skip-button-modern, .ytp-ad-skip-button-slot button'
            );
            skipBtns.forEach(b => { try { b.click(); } catch(e){} });

            const video = document.querySelector('video');
            const adShowing = document.querySelector('.ad-showing, .ad-interrupting');
            const adText = document.querySelector('.ytp-ad-text');
            if (video && (adShowing || adText)) {
                video.muted = true;
                video.playbackRate = 16.0;
                if (video.duration && isFinite(video.duration) && video.currentTime < video.duration) {
                    video.currentTime = video.duration;
                }
            }

            const companions = document.querySelectorAll(
                '#companion, ytd-companion-slot-renderer, ytd-action-companion-ad-renderer, #player-ads, ytd-ad-slot-renderer'
            );
            companions.forEach(c => { try { c.remove(); } catch(e){} });

            const dialog = document.querySelector('tp-yt-paper-dialog:has(ytd-enforcement-message-view-model), ytd-popup-container:has(ytd-enforcement-message-view-model)');
            if (dialog) { dialog.remove(); }
            const backdrop = document.querySelector('tp-yt-iron-overlay-backdrop');
            if (backdrop) { backdrop.remove(); }
        } catch(e) {}
    }

    setInterval(cleanWatchdog, 50);
})();
";
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(jsCode);
                webView.CoreWebView2.Navigate("https://www.youtube.com");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error initializing: " + ex.Message);
            }
        }

        private void ToggleFullScreen()
        {
            if (isFullScreen)
            {
                if (webView != null && webView.CoreWebView2 != null && webView.CoreWebView2.ContainsFullScreenElement)
                {
                    webView.ExecuteScriptAsync("if(document.fullscreenElement) document.exitFullscreen();");
                }
                else
                {
                    ExitFullScreen();
                }
            }
            else
            {
                EnterFullScreen();
            }
        }

        private void EnterFullScreen()
        {
            if (!isFullScreen)
            {
                isFullScreen = true;
                previousWindowState = this.WindowState;
                previousBounds = this.Bounds;

                titleBarPanel.Visible = false;
                this.WindowState = FormWindowState.Normal;
                this.WindowState = FormWindowState.Maximized;
            }
        }

        private void ExitFullScreen()
        {
            if (isFullScreen)
            {
                isFullScreen = false;
                titleBarPanel.Visible = true;
                titleBarPanel.SendToBack();
                this.WindowState = FormWindowState.Normal;
                this.WindowState = previousWindowState;
                if (previousWindowState == FormWindowState.Normal)
                {
                    this.Bounds = previousBounds;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isDownloading && activeDownloadProcess != null)
            {
                var dr = MessageBox.Show(this, "Unduhan video masih berlangsung. Batalkan dan keluar?", "Konfirmasi Keluar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                try { activeDownloadProcess.Kill(); } catch { }
            }
            base.OnFormClosing(e);
        }

        private async void StartDownload()
        {
            if (isDownloading)
            {
                MessageBox.Show(this, "Proses unduhan sedang berjalan. Harap tunggu hingga selesai.", "YouTube Lite", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string currentUrl = (webView != null && webView.CoreWebView2 != null) ? webView.CoreWebView2.Source : "";
            if (string.IsNullOrEmpty(currentUrl) || (!currentUrl.Contains("watch?v=") && !currentUrl.Contains("/shorts/")))
            {
                MessageBox.Show(this, "Silakan putar video atau Shorts yang ingin diunduh terlebih dahulu.", "YouTube Lite Downloader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsDir))
            {
                downloadsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            string ytdlpExe = FindExecutable("yt-dlp.exe");
            if (string.IsNullOrEmpty(ytdlpExe))
            {
                MessageBox.Show(this, "yt-dlp tidak ditemukan di sistem. Pastikan yt-dlp sudah terpasang.", "YouTube Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string ffmpegExe = FindExecutable("ffmpeg.exe");
            string ffmpegArgs = "";
            if (!string.IsNullOrEmpty(ffmpegExe))
            {
                string ffmpegDir = Path.GetDirectoryName(ffmpegExe);
                if (!string.IsNullOrEmpty(ffmpegDir) && Directory.Exists(ffmpegDir))
                {
                    ffmpegArgs = string.Format(" --ffmpeg-location \"{0}\"", ffmpegDir);
                }
            }

            isDownloading = true;
            btnDownload.Text = "⏳";
            btnDownload.ForeColor = Color.FromArgb(234, 179, 8);
            string originalTitle = lblVideoTitle.Text;
            lblVideoTitle.Text = "⬇ Menyiapkan unduhan 1080p...";

            string tempCookieFile = null;
            try
            {
                tempCookieFile = await ExportCookiesToNetscapeFileAsync();
                string cookieArg = "";
                if (!string.IsNullOrEmpty(tempCookieFile) && File.Exists(tempCookieFile))
                {
                    cookieArg = string.Format(" --cookies \"{0}\"", tempCookieFile);
                }

                string userAgentArg = "";
                try
                {
                    string rawUa = await webView.ExecuteScriptAsync("navigator.userAgent");
                    if (!string.IsNullOrEmpty(rawUa))
                    {
                        string cleanUa = rawUa.Trim('\"', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(cleanUa) && cleanUa != "null")
                        {
                            userAgentArg = string.Format(" --user-agent \"{0}\"", cleanUa);
                        }
                    }
                }
                catch { }

                string ytArgs = string.Format(
                    "-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]\" --merge-output-format mp4 -o \"%(title)s [%(id)s].%(ext)s\" --no-playlist --newline{0}{1}{2} -P \"{3}\" \"{4}\"",
                    ffmpegArgs,
                    cookieArg,
                    userAgentArg,
                    downloadsDir,
                    currentUrl
                );

                string lastError = "";

                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ytdlpExe,
                        Arguments = ytArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var proc = new Process { StartInfo = psi })
                    {
                        activeDownloadProcess = proc;

                        proc.OutputDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                string line = args.Data;
                                var match = Regex.Match(line, @"\[download\]\s+(\d+(?:\.\d+)?)%");
                                if (match.Success)
                                {
                                    string pct = match.Groups[1].Value;
                                    this.BeginInvoke((MethodInvoker)delegate
                                    {
                                        lblVideoTitle.Text = string.Format("⬇ Mengunduh 1080p: {0}% ({1})", pct, Path.GetFileName(downloadsDir));
                                    });
                                }
                                else if (line.Contains("[Merger]") || line.Contains("Merging formats into"))
                                {
                                    this.BeginInvoke((MethodInvoker)delegate
                                    {
                                        lblVideoTitle.Text = "⚙ Menggabungkan video & audio 1080p (ffmpeg)...";
                                    });
                                }
                            }
                        };

                        proc.ErrorDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                lastError = args.Data;
                            }
                        };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        proc.WaitForExit();

                        activeDownloadProcess = null;

                        if (proc.ExitCode != 0)
                        {
                            throw new Exception(!string.IsNullOrEmpty(lastError) ? lastError : ("Kode keluar: " + proc.ExitCode));
                        }
                    }
                });

                btnDownload.Text = "✓";
                btnDownload.ForeColor = Color.FromArgb(34, 197, 94);
                lblVideoTitle.Text = "✓ Unduhan 1080p selesai! Tersimpan di Downloads.";

                DialogResult dr = MessageBox.Show(this,
                    "Video 1080p berhasil diunduh dan tersimpan di folder Downloads!\n\nBuka folder Downloads sekarang?",
                    "Unduhan Berhasil",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (dr == DialogResult.Yes)
                {
                    try { Process.Start("explorer.exe", downloadsDir); } catch { }
                }
            }
            catch (Exception ex)
            {
                btnDownload.Text = "✕";
                btnDownload.ForeColor = Color.FromArgb(239, 68, 68);
                lblVideoTitle.Text = "Gagal mengunduh video.";
                string msg = ex.Message;
                if (msg.Contains("Sign in to confirm your age"))
                {
                    msg = "Video ini memiliki pembatasan usia (Age-Restricted) dari YouTube.\n\nPastikan akun Google/YouTube sudah login di aplikasi YouTube Lite ini dan sudah terverifikasi usia 18+.";
                }
                MessageBox.Show(this, "Gagal mengunduh video:\n\n" + msg, "YouTube Lite Downloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isDownloading = false;
                activeDownloadProcess = null;

                if (!string.IsNullOrEmpty(tempCookieFile) && File.Exists(tempCookieFile))
                {
                    try { File.Delete(tempCookieFile); } catch { }
                }

                var timer = new System.Windows.Forms.Timer { Interval = 4500 };
                timer.Tick += (s, e) =>
                {
                    btnDownload.Text = "⬇";
                    btnDownload.ForeColor = Color.FromArgb(160, 165, 180);
                    if (lblVideoTitle.Text.StartsWith("✓") || lblVideoTitle.Text.StartsWith("Gagal"))
                    {
                        lblVideoTitle.Text = originalTitle;
                    }
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
        }

        private async Task<string> ExportCookiesToNetscapeFileAsync()
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null) return null;

                var cookieDict = new Dictionary<string, CoreWebView2Cookie>();

                List<CoreWebView2Cookie> ytCookies = null;
                try
                {
                    ytCookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com");
                }
                catch { }

                if (ytCookies != null)
                {
                    foreach (var c in ytCookies)
                    {
                        string key = (c.Domain ?? "") + "|" + (c.Path ?? "") + "|" + (c.Name ?? "");
                        cookieDict[key] = c;
                    }
                }

                List<CoreWebView2Cookie> gCookies = null;
                try
                {
                    gCookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://accounts.google.com");
                }
                catch { }

                if (gCookies != null)
                {
                    foreach (var c in gCookies)
                    {
                        string key = (c.Domain ?? "") + "|" + (c.Path ?? "") + "|" + (c.Name ?? "");
                        cookieDict[key] = c;
                    }
                }

                List<CoreWebView2Cookie> googleRootCookies = null;
                try
                {
                    googleRootCookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.google.com");
                }
                catch { }

                if (googleRootCookies != null)
                {
                    foreach (var c in googleRootCookies)
                    {
                        string key = (c.Domain ?? "") + "|" + (c.Path ?? "") + "|" + (c.Name ?? "");
                        cookieDict[key] = c;
                    }
                }

                if (cookieDict.Count == 0) return null;

                string tempFile = Path.Combine(Path.GetTempPath(), "ytlite_cookies_" + Guid.NewGuid().ToString("N") + ".txt");
                using (var sw = new StreamWriter(tempFile, false, new System.Text.UTF8Encoding(false)))
                {
                    sw.NewLine = "\n";
                    sw.WriteLine("# Netscape HTTP Cookie File");
                    sw.WriteLine("# http://curl.haxx.se/rfc/cookie_spec.html");
                    sw.WriteLine("# This file was generated by YouTube Lite! Edit at your own risk.");
                    sw.WriteLine();

                    long defaultExpiry = (long)(DateTime.UtcNow.AddYears(1) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

                    foreach (var kvp in cookieDict)
                    {
                        var c = kvp.Value;
                        long expiry = defaultExpiry;
                        try
                        {
                            if (!c.IsSession && c.Expires > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                            {
                                expiry = (long)(c.Expires.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                            }
                        }
                        catch { }

                        string domain = c.Domain ?? ".youtube.com";
                        string httpOnlyPrefix = c.IsHttpOnly ? "#HttpOnly_" : "";
                        string subdomains = domain.StartsWith(".") ? "TRUE" : "FALSE";
                        string path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;
                        string secure = c.IsSecure ? "TRUE" : "FALSE";
                        string name = (c.Name ?? "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
                        string val = (c.Value ?? "").Replace("\t", "").Replace("\r", "").Replace("\n", "");

                        if (string.IsNullOrEmpty(name)) continue;

                        sw.WriteLine(string.Format("{0}{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}",
                            httpOnlyPrefix,
                            domain,
                            subdomains,
                            path,
                            secure,
                            expiry,
                            name,
                            val
                        ));
                    }
                }
                return tempFile;
            }
            catch
            {
                return null;
            }
        }

        private string FindExecutable(string name)
        {
            try
            {
                var psi = new ProcessStartInfo("where.exe", name)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && File.Exists(lines[0].Trim()))
                            return lines[0].Trim();
                    }
                }
            }
            catch { }

            string localBin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (File.Exists(localBin)) return localBin;

            return name;
        }
    }
}
