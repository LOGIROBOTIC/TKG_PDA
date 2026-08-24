import 'package:flutter/material.dart';

import 'app/app.dart';
import 'app/app_controller.dart';
import 'core/auth/session_memory.dart';
import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'core/storage/device_code_store.dart';
import 'data/repositories/pda_repository.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final session = SessionMemory();
  late final AppController controller;
  final api = ApiClient(
    baseUri: AppConfig.fromEnvironment.apiBaseUri,
    deviceCode: () => controller.deviceCode,
    accessToken: () => session.accessToken,
    onUnauthorized: () => controller.handleUnauthorized(),
  );
  controller = AppController(
    repository: PdaRepository(api),
    deviceCodeStore: SecureDeviceCodeStore(),
    session: session,
  );
  await controller.initialize();
  runApp(TkgPdaApp(controller: controller));
}
