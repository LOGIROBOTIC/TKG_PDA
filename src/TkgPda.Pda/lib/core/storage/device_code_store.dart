abstract interface class DeviceCodeStore {
  static const persistentKey = 'tkg.pda.device-code';

  Future<String?> read();
  Future<void> write(String deviceCode);
  Future<void> clear();
}

abstract interface class StringKeyValueStore {
  String? read(String key);
  void write(String key, String value);
  void remove(String key);
}

class PersistentDeviceCodeStore implements DeviceCodeStore {
  PersistentDeviceCodeStore(this._storage);

  final StringKeyValueStore _storage;

  @override
  Future<String?> read() async {
    final normalized = _storage.read(DeviceCodeStore.persistentKey)?.trim();
    return normalized == null || normalized.isEmpty ? null : normalized;
  }

  @override
  Future<void> write(String deviceCode) async {
    _storage.write(DeviceCodeStore.persistentKey, deviceCode.trim());
  }

  @override
  Future<void> clear() async {
    _storage.remove(DeviceCodeStore.persistentKey);
  }
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
