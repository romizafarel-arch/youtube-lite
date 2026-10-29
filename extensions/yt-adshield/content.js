(function () {
  'use strict';

  // 1. Ensure Clean CSS Stylesheet is ALWAYS active
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
          #masthead-ad,
          ytd-ad-slot-renderer,
          ytd-banner-promo-renderer,
          ytd-rich-item-renderer:has(ytd-ad-slot-renderer),
          ytd-rich-section-renderer:has(ytd-ad-slot-renderer),
          ytd-rich-item-renderer:has(#ad-badge),
          ytd-rich-item-renderer:has(.badge-shape-wiz--thumbnail-badge),
          ytd-rich-item-renderer:has([aria-label*="Bersponsor"]),
          ytd-rich-item-renderer:has([aria-label*="Sponsored"]),
          #player-ads,
          #companion,
          ytd-companion-slot-renderer,
          ytd-action-companion-ad-renderer,
          ytd-promoted-sparkles-web-renderer,
          ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-ads"],
          .ytp-ad-module,
          .ytp-ad-overlay-container,
          .ytp-ad-message-container,
          ytd-in-feed-ad-layout-renderer,
          tp-yt-paper-dialog:has(ytd-enforcement-message-view-model),
          ytd-popup-container:has(ytd-enforcement-message-view-model),
          tp-yt-iron-overlay-backdrop {
            display: none !important;
          }
        `;
        target.appendChild(s);
      }
    } catch (e) {}
  }
  ensureStyle();
  document.addEventListener('DOMContentLoaded', ensureStyle);

  // 2. Brave-Style JSON Pruning Defuser
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
    } catch (e) {}
    return obj;
  }

  // Hook ytInitialPlayerResponse
  let _ytInitialPlayerResponse = window.ytInitialPlayerResponse;
  Object.defineProperty(window, 'ytInitialPlayerResponse', {
    get() {
      return _ytInitialPlayerResponse;
    },
    set(val) {
      _ytInitialPlayerResponse = prunePlayerObject(val);
    },
    configurable: true,
    enumerable: true
  });

  // Hook ytplayer
  let _ytplayer = window.ytplayer;
  Object.defineProperty(window, 'ytplayer', {
    get() {
      return _ytplayer;
    },
    set(val) {
      if (val && val.config && val.config.args && val.config.args.raw_player_response) {
        prunePlayerObject(val.config.args.raw_player_response);
      }
      _ytplayer = val;
    },
    configurable: true,
    enumerable: true
  });

  // Hook JSON.parse
  const origJSONParse = JSON.parse;
  JSON.parse = function (text, reviver) {
    const data = origJSONParse.apply(this, arguments);
    if (data && typeof data === 'object') {
      prunePlayerObject(data);
    }
    return data;
  };

  // Hook Response.prototype.json
  if (window.Response && Response.prototype.json) {
    const origResponseJson = Response.prototype.json;
    Response.prototype.json = async function () {
      const data = await origResponseJson.apply(this, arguments);
      if (data && typeof data === 'object') {
        prunePlayerObject(data);
      }
      return data;
    };
  }

  // 3. Active Watchdog & Instant Ad Annihilator (Catches unskippable & SSAI ads immediately)
  function cleanWatchdog() {
    try {
      ensureStyle();

      // Native player instant skip
      const player = document.getElementById('movie_player');
      if (player && typeof player.skipAd === 'function') {
        player.skipAd();
      }

      // Click skip buttons immediately
      const skipBtns = document.querySelectorAll(
        '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, .videoAdUiSkipButton, button.ytp-ad-skip-button-modern, .ytp-ad-skip-button-slot button'
      );
      skipBtns.forEach((b) => {
        try {
          b.click();
        } catch (e) {}
      });

      // If ad is playing on video element, speed it up & jump to duration
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

      // Remove companion & sidebar banners from DOM
      const companions = document.querySelectorAll(
        '#companion, ytd-companion-slot-renderer, ytd-action-companion-ad-renderer, #player-ads, ytd-ad-slot-renderer'
      );
      companions.forEach((c) => {
        try {
          c.remove();
        } catch (e) {}
      });

      // Dismiss anti-adblock modals
      const dialog = document.querySelector(
        'tp-yt-paper-dialog:has(ytd-enforcement-message-view-model), ytd-popup-container:has(ytd-enforcement-message-view-model)'
      );
      if (dialog) {
        dialog.remove();
      }
      const backdrop = document.querySelector('tp-yt-iron-overlay-backdrop');
      if (backdrop) {
        backdrop.remove();
      }
    } catch (e) {}
  }

  setInterval(cleanWatchdog, 50);
})();
