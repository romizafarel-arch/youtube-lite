package com.romi.youtubelite;

import android.annotation.SuppressLint;
import android.app.PictureInPictureParams;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Rational;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.ImageButton;
import android.widget.LinearLayout;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import java.io.ByteArrayInputStream;
import java.nio.charset.StandardCharsets;

public class MainActivity extends AppCompatActivity {

    private static final String YOUTUBE_URL = "https://m.youtube.com";

    // Known ad networks and tracking endpoints
    private static final String[] AD_HOSTS = new String[] {
        "doubleclick.net",
        "googleads.g.doubleclick.net",
        "pagead2.googlesyndication.com",
        "ad.doubleclick.net",
        "pubads.g.doubleclick.net",
        "youtube.com/api/stats/ads",
        "youtube.com/pagead/",
        "youtube.com/api/stats/atr",
        "google.com/pagead"
    };

    // Client-side script: Adblock CSS, Video Ad auto-skip, & Background Play spoofing
    private static final String AD_BLOCK_AND_BACKGROUND_JS =
        "(function() {" +
        "  var style = document.getElementById('yt-lite-adblock-style');" +
        "  if (!style) {" +
        "    style = document.createElement('style');" +
        "    style.id = 'yt-lite-adblock-style';" +
        "    style.textContent = `" +
        "      .ytp-ad-overlay-container, .ytp-ad-message-container," +
        "      .ad-container, .ad-div, ytm-promoted-sparkles-web-renderer," +
        "      ytd-ad-slot-renderer, ytm-companion-ad-renderer," +
        "      .ytp-ad-module, .ad-showing," +
        "      ytm-item-section-renderer[section-identifier='comment-item-section'] + ytm-companion-ad-renderer {" +
        "        display: none !important;" +
        "      }" +
        "      .ytp-caption-window-container, .ytp-caption-window-bottom, .caption-window {" +
        "        display: block !important; visibility: visible !important; opacity: 1 !important; z-index: 60 !important;" +
        "      }" +
        "      .ytp-caption-segment {" +
        "        display: inline-block !important; visibility: visible !important;" +
        "      }" +
        "    `;" +
        "    (document.head || document.documentElement).appendChild(style);" +
        "  }" +
        "" +
        "  try {" +
        "    Object.defineProperty(document, 'hidden', { value: false, writable: false });" +
        "    Object.defineProperty(document, 'visibilityState', { value: 'visible', writable: false });" +
        "    window.addEventListener('visibilitychange', function(e) {" +
        "      e.stopImmediatePropagation();" +
        "    }, true);" +
        "  } catch(e) {}" +
        "" +
        "  if (!window.__ytLiteObserverActive) {" +
        "    window.__ytLiteObserverActive = true;" +
        "    setInterval(function() {" +
        "      var skipBtns = document.querySelectorAll('.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-ad-skip-button-modern, button.ytp-ad-skip-button');" +
        "      for (var i = 0; i < skipBtns.length; i++) {" +
        "        skipBtns[i].click();" +
        "      }" +
        "      var video = document.querySelector('video');" +
        "      var adShowing = document.querySelector('.ad-showing, .ad-interrupting');" +
        "      if (video && adShowing) {" +
        "        video.playbackRate = 16.0;" +
        "        video.muted = true;" +
        "        if (!isNaN(video.duration) && video.duration > 0) {" +
        "          video.currentTime = video.duration;" +
        "        }" +
        "      }" +
        "    }, 350);" +
        "  }" +
        "})();";

    // Intercepts click events on video anchors in SPA mode before YouTube router can suppress
    private static final String INTERCEPT_VIDEO_CLICKS_JS =
        "(function() {" +
        "  if (window.__ytLiteInterceptActive) return;" +
        "  window.__ytLiteInterceptActive = true;" +
        "  document.addEventListener('click', function(e) {" +
        "    var a = e.target.closest('a');" +
        "    if (a && a.href) {" +
        "      var h = a.href;" +
        "      if (h.indexOf('/watch?v=') !== -1 || h.indexOf('/shorts/') !== -1) {" +
        "        e.preventDefault();" +
        "        e.stopPropagation();" +
        "        window.AndroidBridge.openVideo(h);" +
        "        return false;" +
        "      }" +
        "    }" +
        "  }, true);" +
        "})();";

    // Mini-player states
    private static final int STATE_CLOSED = 0;
    private static final int STATE_EXPANDED = 1;
    private static final int STATE_MINIMIZED = 2;

    private int playerState = STATE_CLOSED;

    private SwipeRefreshLayout feedSwipeRefresh;
    private WebView feedWebView;

    private FrameLayout playerContainer;
    private WebView playerWebView;
    private LinearLayout playerHeaderControls;
    private FrameLayout miniTouchOverlay;
    private ImageButton btnMinimize;
    private ImageButton btnClose;
    private ImageButton btnMiniClose;

    private FrameLayout fullscreenContainer;
    private View customView;
    private WebChromeClient.CustomViewCallback customViewCallback;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        feedSwipeRefresh = findViewById(R.id.feedSwipeRefresh);
        feedWebView = findViewById(R.id.feedWebView);

        playerContainer = findViewById(R.id.playerContainer);
        playerWebView = findViewById(R.id.playerWebView);
        playerHeaderControls = findViewById(R.id.playerHeaderControls);
        miniTouchOverlay = findViewById(R.id.miniTouchOverlay);
        btnMinimize = findViewById(R.id.btnMinimize);
        btnClose = findViewById(R.id.btnClose);
        btnMiniClose = findViewById(R.id.btnMiniClose);

        fullscreenContainer = findViewById(R.id.fullscreenContainer);

        configureFeedWebView();
        configurePlayerWebView();
        setupPlayerControls();

        feedSwipeRefresh.setColorSchemeResources(R.color.primary_red);
        feedSwipeRefresh.setProgressBackgroundColorSchemeResource(R.color.bg_dark);
        feedSwipeRefresh.setOnRefreshListener(() -> feedWebView.reload());

        // Prevent pull-to-refresh conflicting with web scrolling
        feedWebView.getViewTreeObserver().addOnScrollChangedListener(() -> {
            if (feedWebView.getScrollY() == 0) {
                feedSwipeRefresh.setEnabled(customView == null && playerState != STATE_EXPANDED);
            } else {
                feedSwipeRefresh.setEnabled(false);
            }
        });

        if (savedInstanceState != null) {
            feedWebView.restoreState(savedInstanceState);
        } else {
            feedWebView.loadUrl(YOUTUBE_URL);
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void configureFeedWebView() {
        applyCommonWebSettings(feedWebView);

        feedWebView.addJavascriptInterface(new Object() {
            @JavascriptInterface
            public void openVideo(String url) {
                runOnUiThread(() -> playVideo(url));
            }
        }, "AndroidBridge");

        feedWebView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                String url = request.getUrl().toString();
                if (url.contains("/watch?v=") || url.contains("/shorts/")) {
                    playVideo(url);
                    return true;
                }
                return false;
            }

            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                if (isAdRequest(request.getUrl())) {
                    return emptyWebResourceResponse();
                }
                return super.shouldInterceptRequest(view, request);
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                super.onPageFinished(view, url);
                feedSwipeRefresh.setRefreshing(false);
                view.evaluateJavascript(AD_BLOCK_AND_BACKGROUND_JS, null);
                view.evaluateJavascript(INTERCEPT_VIDEO_CLICKS_JS, null);
            }
        });
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void configurePlayerWebView() {
        applyCommonWebSettings(playerWebView);

        playerWebView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                String url = request.getUrl().toString();
                // If a new video is clicked inside recommendations, play in current player
                if (url.contains("/watch?v=") || url.contains("/shorts/")) {
                    playVideo(url);
                    return true;
                }
                // If channel or external link is clicked, load on feed webview and minimize player
                feedWebView.loadUrl(url);
                minimizePlayer();
                return true;
            }

            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                if (isAdRequest(request.getUrl())) {
                    return emptyWebResourceResponse();
                }
                return super.shouldInterceptRequest(view, request);
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                super.onPageFinished(view, url);
                view.evaluateJavascript(AD_BLOCK_AND_BACKGROUND_JS, null);
            }
        });

        playerWebView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onShowCustomView(View view, CustomViewCallback callback) {
                if (customView != null) {
                    onHideCustomView();
                    return;
                }

                customView = view;
                customViewCallback = callback;

                fullscreenContainer.addView(customView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
                fullscreenContainer.setVisibility(View.VISIBLE);
                playerContainer.setVisibility(View.GONE);
                feedSwipeRefresh.setVisibility(View.GONE);
                feedSwipeRefresh.setEnabled(false);

                setImmersiveFullscreen(true);
            }

            @Override
            public void onHideCustomView() {
                if (customView == null) return;

                fullscreenContainer.removeView(customView);
                fullscreenContainer.setVisibility(View.GONE);
                customView = null;

                if (customViewCallback != null) {
                    customViewCallback.onCustomViewHidden();
                    customViewCallback = null;
                }

                if (playerState != STATE_CLOSED) {
                    playerContainer.setVisibility(View.VISIBLE);
                }
                feedSwipeRefresh.setVisibility(View.VISIBLE);
                feedSwipeRefresh.setEnabled(playerState != STATE_EXPANDED);

                setImmersiveFullscreen(false);
            }
        });
    }

    private void applyCommonWebSettings(WebView wv) {
        WebSettings settings = wv.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setLoadsImagesAutomatically(true);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setSupportZoom(true);
        settings.setBuiltInZoomControls(true);
        settings.setDisplayZoomControls(false);

        CookieManager cookieManager = CookieManager.getInstance();
        cookieManager.setAcceptCookie(true);
        cookieManager.setAcceptThirdPartyCookies(wv, true);

        String rawUa = settings.getUserAgentString();
        String cleanUa = rawUa
            .replace("; wv", "")
            .replaceAll("Version/\\d+\\.\\d+\\s*", "");
        settings.setUserAgentString(cleanUa);
    }

    private void setupPlayerControls() {
        btnMinimize.setOnClickListener(v -> minimizePlayer());
        btnClose.setOnClickListener(v -> closePlayer());

        miniTouchOverlay.setOnClickListener(v -> expandPlayer());
        btnMiniClose.setOnClickListener(v -> closePlayer());
    }

    public void playVideo(String url) {
        // Normalize shorts to standard watch url for better controls
        if (url.contains("/shorts/")) {
            String id = url.substring(url.indexOf("/shorts/") + 8);
            if (id.contains("?")) id = id.substring(0, id.indexOf("?"));
            url = "https://m.youtube.com/watch?v=" + id;
        }

        playerWebView.loadUrl(url);
        expandPlayer();
    }

    public void expandPlayer() {
        playerState = STATE_EXPANDED;
        playerContainer.setVisibility(View.VISIBLE);

        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        playerContainer.setLayoutParams(params);
        playerContainer.setBackgroundResource(R.color.black);
        playerContainer.setElevation(0);

        playerHeaderControls.setVisibility(View.VISIBLE);
        miniTouchOverlay.setVisibility(View.GONE);
        feedSwipeRefresh.setEnabled(false);
    }

    public void minimizePlayer() {
        playerState = STATE_MINIMIZED;
        playerContainer.setVisibility(View.VISIBLE);

        float density = getResources().getDisplayMetrics().density;
        int widthPx = (int) (200 * density);
        int heightPx = (int) (112 * density);
        int marginPx = (int) (16 * density);
        int bottomMarginPx = (int) (24 * density);

        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(widthPx, heightPx);
        params.gravity = Gravity.BOTTOM | Gravity.END;
        params.setMargins(0, 0, marginPx, bottomMarginPx);

        playerContainer.setLayoutParams(params);
        playerContainer.setBackgroundResource(R.drawable.bg_miniplayer);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            playerContainer.setElevation(12 * density);
            playerContainer.setClipToOutline(true);
        }

        playerHeaderControls.setVisibility(View.GONE);
        miniTouchOverlay.setVisibility(View.VISIBLE);
        feedSwipeRefresh.setEnabled(feedWebView.getScrollY() == 0);
    }

    public void closePlayer() {
        playerState = STATE_CLOSED;
        playerWebView.loadUrl("about:blank");
        playerContainer.setVisibility(View.GONE);
        feedSwipeRefresh.setEnabled(feedWebView.getScrollY() == 0);
    }

    private boolean isAdRequest(Uri uri) {
        if (uri == null) return false;
        String hostAndPath = (uri.getHost() + uri.getPath()).toLowerCase();
        for (String adHost : AD_HOSTS) {
            if (hostAndPath.contains(adHost)) {
                return true;
            }
        }
        return false;
    }

    private WebResourceResponse emptyWebResourceResponse() {
        return new WebResourceResponse("text/plain", "UTF-8",
            new ByteArrayInputStream("".getBytes(StandardCharsets.UTF_8)));
    }

    private void setImmersiveFullscreen(boolean enabled) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) {
                if (enabled) {
                    controller.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                    controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                } else {
                    controller.show(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                }
            }
        } else {
            View decorView = getWindow().getDecorView();
            if (enabled) {
                decorView.setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
            } else {
                decorView.setSystemUiVisibility(View.SYSTEM_UI_FLAG_VISIBLE);
            }
        }
    }

    @Override
    protected void onUserLeaveHint() {
        super.onUserLeaveHint();
        // Trigger Picture-in-Picture on Home press if player is active (Android 8.0+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && playerState != STATE_CLOSED) {
            try {
                PictureInPictureParams params = new PictureInPictureParams.Builder()
                    .setAspectRatio(new Rational(16, 9))
                    .build();
                enterPictureInPictureMode(params);
            } catch (Exception ignored) {
            }
        }
    }

    @Override
    public void onPictureInPictureModeChanged(boolean isInPictureInPictureMode, @NonNull Configuration newConfig) {
        super.onPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);
        if (isInPictureInPictureMode) {
            feedSwipeRefresh.setVisibility(View.GONE);
            playerHeaderControls.setVisibility(View.GONE);
            miniTouchOverlay.setVisibility(View.GONE);
            FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
            playerContainer.setLayoutParams(params);
        } else {
            feedSwipeRefresh.setVisibility(View.VISIBLE);
            if (playerState == STATE_MINIMIZED) {
                minimizePlayer();
            } else if (playerState == STATE_EXPANDED) {
                expandPlayer();
            }
        }
    }

    @Override
    public void onBackPressed() {
        // 1. Exit HTML5 Fullscreen if active
        if (customView != null) {
            if (playerWebView.getWebChromeClient() != null) {
                ((WebChromeClient) playerWebView.getWebChromeClient()).onHideCustomView();
            }
            return;
        }

        // 2. If watching video in Expanded mode, minimize to floating dock (Native YouTube app UX)
        if (playerState == STATE_EXPANDED) {
            minimizePlayer();
            return;
        }

        // 3. If in Minimized mode or Closed, navigate feed history
        if (feedWebView.canGoBack()) {
            feedWebView.goBack();
        } else {
            super.onBackPressed();
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        // Crucial: Do NOT pause playerWebView so background playback stays active!
    }

    @Override
    protected void onDestroy() {
        if (playerWebView != null) {
            playerWebView.destroy();
        }
        if (feedWebView != null) {
            feedWebView.destroy();
        }
        super.onDestroy();
    }

    @Override
    protected void onSaveInstanceState(@NonNull Bundle outState) {
        super.onSaveInstanceState(outState);
        feedWebView.saveState(outState);
    }
}
