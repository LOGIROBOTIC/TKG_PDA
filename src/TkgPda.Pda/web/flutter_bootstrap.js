{{flutter_js}}
{{flutter_build_config}}

_flutter.loader.load({
  config: {
    // Flutter's engine otherwise fetches fallback glyph fonts from
    // fonts.gstatic.com. Production-line clients must remain LAN-only.
    fontFallbackBaseUrl: 'font-fallbacks/',
  },
});

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('operator_service_worker.js', {
      scope: './',
    });
  });
}
