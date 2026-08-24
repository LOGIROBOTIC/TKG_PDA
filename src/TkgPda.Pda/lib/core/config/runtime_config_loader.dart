import 'runtime_config_loader_stub.dart'
    if (dart.library.js_interop) 'runtime_config_loader_web.dart'
    as implementation;

class RuntimeAppConfig {
  const RuntimeAppConfig({this.apiBaseUrl});

  final String? apiBaseUrl;
}

abstract interface class RuntimeConfigLoader {
  Future<RuntimeAppConfig> load();
}

RuntimeConfigLoader createRuntimeConfigLoader() =>
    implementation.createRuntimeConfigLoader();
