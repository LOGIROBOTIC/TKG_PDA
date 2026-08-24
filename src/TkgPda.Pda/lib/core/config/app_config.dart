class AppConfig {
  const AppConfig({required this.apiBaseUrl});

  final String apiBaseUrl;

  static const fromEnvironment = AppConfig(
    apiBaseUrl: String.fromEnvironment(
      'TKG_API_BASE_URL',
      defaultValue: 'http://10.0.2.2:5000',
    ),
  );

  Uri get apiBaseUri {
    final uri = Uri.tryParse(apiBaseUrl);
    if (uri == null ||
        !{'http', 'https'}.contains(uri.scheme.toLowerCase()) ||
        uri.host.isEmpty) {
      throw StateError(
        'TKG_API_BASE_URL geçerli bir HTTP(S) adresi olmalıdır.',
      );
    }
    return uri;
  }
}
