import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'device_code_store.dart';

class SecureDeviceCodeStore implements DeviceCodeStore {
  SecureDeviceCodeStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() async {
    final value = await _storage.read(key: DeviceCodeStore.persistentKey);
    final normalized = value?.trim();
    return normalized == null || normalized.isEmpty ? null : normalized;
  }

  @override
  Future<void> write(String deviceCode) => _storage.write(
    key: DeviceCodeStore.persistentKey,
    value: deviceCode.trim(),
  );

  @override
  Future<void> clear() => _storage.delete(key: DeviceCodeStore.persistentKey);
}
