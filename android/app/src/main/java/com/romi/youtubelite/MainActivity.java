package com.romi.youtubelite;

import android.annotation.SuppressLint;
import android.app.PictureInPictureParams;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Rational;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

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

    private WebView webView;
    private SwipeRefreshLayout swipeRefresh;
    private FrameLayout fullscreenContainer;
    private View customView;
    private WebChromeClient.CustomViewCallback customViewCallback;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        swipeRefresh = findViewById(R.id.swipeRefreshLayout);
        webView = findViewById(R.id.webView);
        fullscreenContainer = findViewById(R.id.fullscreenContainer);

        configureWebView();

        swipeRefresh.setColorSchemeResources(R.color.primary_red);
        swipeRefresh.setProgressBackgroundColorSchemeResource(R.color.bg_dark);
        swipeRefresh.setOnRefreshListener(() -> webView.reload());

        // Prevent pull-to-refresh conflicting with web scrolling
        webView.getViewTreeObserver().addOnScrollChangedListener(() -> {
            if (webView.getScrollY() == 0) {
                swipeRefresh.setEnabled(customView == null);
            } else {
                swipeRefresh.setEnabled(false);
            }
        });

        if (savedInstanceState != null) {
            webView.restoreState(savedInstanceState);
        } else {
            webView.loadUrl(YOUTUBE_URL);
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void configureWebView() {
        WebSettings settings = webView.getSettings();
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

        // Keep default mobile Chrome User-Agent for native responsive layout
        String defaultUa = settings.getUserAgentString();
        settings.setUserAgentString(defaultUa.replace("; wv", "")); // Remove WebView marker

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                String hostAndPath = (uri.getHost() + uri.getPath()).toLowerCase();

                for (String adHost : AD_HOSTS) {
                    if (hostAndPath.contains(adHost)) {
                        return new WebResourceResponse("text/plain", "UTF-8",
                            new ByteArrayInputStream("".getBytes(StandardCharsets.UTF_8)));
                    }
                }
                return super.shouldInterceptRequest(view, request);
            }

            @Override
            public void onPageStarted(WebView view, String url, Bitmap favicon) {
                super.onPageStarted(view, url, favicon);
                view.evaluateJavascript(AD_BLOCK_AND_BACKGROUND_JS, null);
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                super.onPageFinished(view, url);
                swipeRefresh.setRefreshing(false);
                view.evaluateJavascript(AD_BLOCK_AND_BACKGROUND_JS, null);
            }
        });

        webView.setWebChromeClient(new WebChromeClient() {
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
                swipeRefresh.setVisibility(View.GONE);
                swipeRefresh.setEnabled(false);

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

                swipeRefresh.setVisibility(View.VISIBLE);
                swipeRefresh.setEnabled(true);

                setImmersiveFullscreen(false);
            }
        });
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
        // Trigger Picture-in-Picture on Home press (Android 8.0+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
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
            swipeRefresh.setEnabled(false);
        } else {
            swipeRefresh.setEnabled(customView == null);
        }
    }

    @Override
    public void onBackPressed() {
        if (customView != null) {
            // Exit fullscreen video
            if (webView.getWebChromeClient() != null) {
                ((WebChromeClient) webView.getWebChromeClient()).onHideCustomView();
            }
            return;
        }

        if (webView.canGoBack()) {
            webView.goBack();
        } else {
            super.onBackPressed();
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        // Crucial: Do NOT call webView.onPause() so background playback stays active!
    }

    @Override
    protected void onSaveInstanceState(@NonNull Bundle outState) {
        super.onSaveInstanceState(outState);
        webView.saveState(outState);
    }
}
