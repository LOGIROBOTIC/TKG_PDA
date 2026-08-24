import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'runtime_config_loader.dart';

RuntimeConfigLoader createRuntimeConfigLoader() => WebRuntimeConfigLoader();

class WebRuntimeConfigLoader implements RuntimeConfigLoader {
  WebRuntimeConfigLoader({http.Client? client, Uri? documentUri})
    : _client = client ?? http.Client(),
      _documentUri = documentUri ?? Uri.base;

  final http.Client _client;
  final Uri _documentUri;

  @override
  Future<RuntimeAppConfig> load() async {
    try {
      final response = await _client
          .get(_documentUri.resolve('operator-config.json'))
          .timeout(const Duration(seconds: 3));
      if (response.statusCode == 404) return const RuntimeAppConfig();
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw StateError(
          'Operator runtime configuration returned HTTP ${response.statusCode}.',
        );
      }
      final decoded = jsonDecode(response.body);
      if (decoded is! Map<String, dynamic>) {
        throw StateError(
          'Operator runtime configuration must be a JSON object.',
        );
      }
      final apiBaseUrl = decoded['apiBaseUrl'];
      if (apiBaseUrl != null && apiBaseUrl is! String) {
        throw StateError('apiBaseUrl must be a string or null.');
      }
      return RuntimeAppConfig(apiBaseUrl: apiBaseUrl as String?);
    } on http.ClientException {
      return const RuntimeAppConfig();
    } on TimeoutException {
      return const RuntimeAppConfig();
    }
  }
}
