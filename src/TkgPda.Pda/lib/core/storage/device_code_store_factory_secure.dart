import 'device_code_store.dart';
import 'device_code_store_secure.dart';

DeviceCodeStore createDeviceCodeStore() => SecureDeviceCodeStore();
