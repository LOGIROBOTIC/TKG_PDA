import 'runtime_config_loader.dart';

RuntimeConfigLoader createRuntimeConfigLoader() =>
    const NoopRuntimeConfigLoader();

class NoopRuntimeConfigLoader implements RuntimeConfigLoader {
  const NoopRuntimeConfigLoader();

  @override
  Future<RuntimeAppConfig> load() async => const RuntimeAppConfig();
}
