import 'package:flutter_secure_storage/flutter_secure_storage.dart';

abstract interface class DeviceCodeStore {
  Future<String?> read();
  Future<void> write(String deviceCode);
  Future<void> clear();
}

class SecureDeviceCodeStore implements DeviceCodeStore {
  SecureDeviceCodeStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'tkg.pda.device-code';
  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() async {
    final value = await _storage.read(key: _key);
    final normalized = value?.trim();
    return normalized == null || normalized.isEmpty ? null : normalized;
  }

  @override
  Future<void> write(String deviceCode) =>
      _storage.write(key: _key, value: deviceCode.trim());

  @override
  Future<void> clear() => _storage.delete(key: _key);
}

class MemoryDeviceCodeStore implements DeviceCodeStore {
  MemoryDeviceCodeStore([this.value]);

  String? value;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String deviceCode) async {
    value = deviceCode.trim();
  }

  @override
  Future<void> clear() async {
    value = null;
  }
}
