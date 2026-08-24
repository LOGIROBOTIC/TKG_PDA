import 'package:web/web.dart' as web;

import 'device_code_store.dart';

class WebDeviceCodeStore extends PersistentDeviceCodeStore {
  WebDeviceCodeStore({StringKeyValueStore? storage})
    : super(storage ?? WebLocalStorage());
}

class WebLocalStorage implements StringKeyValueStore {
  @override
  String? read(String key) => web.window.localStorage.getItem(key);

  @override
  void remove(String key) => web.window.localStorage.removeItem(key);

  @override
  void write(String key, String value) =>
      web.window.localStorage.setItem(key, value);
}
