import 'device_code_store.dart';
import 'device_code_store_factory_stub.dart'
    if (dart.library.io) 'device_code_store_factory_secure.dart'
    if (dart.library.js_interop) 'device_code_store_factory_web.dart'
    as implementation;

DeviceCodeStore createDeviceCodeStore() =>
    implementation.createDeviceCodeStore();
