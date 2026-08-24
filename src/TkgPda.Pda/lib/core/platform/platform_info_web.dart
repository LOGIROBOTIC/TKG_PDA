import 'platform_info.dart';

PlatformInfo createPlatformInfo() {
  final base = Uri.base;
  return PlatformInfo(
    isWeb: true,
    browserOrigin: Uri(scheme: base.scheme, host: base.host, port: base.port),
  );
}
