import 'platform_info_stub.dart'
    if (dart.library.io) 'platform_info_io.dart'
    if (dart.library.js_interop) 'platform_info_web.dart'
    as implementation;

class PlatformInfo {
  const PlatformInfo({required this.isWeb, this.browserOrigin});

  final bool isWeb;
  final Uri? browserOrigin;
}

PlatformInfo createPlatformInfo() => implementation.createPlatformInfo();
