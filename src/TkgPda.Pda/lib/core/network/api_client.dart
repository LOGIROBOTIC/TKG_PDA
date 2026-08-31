import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import '../errors/api_problem.dart';
import '../../data/models/pda_models.dart';

class ApiResponse {
  const ApiResponse({required this.statusCode, required this.body});

  final int statusCode;
  final Object? body;
}

class ApiClient {
  ApiClient({
    required Uri baseUri,
    required String? Function() deviceCode,
    required String? Function() accessToken,
    required void Function() onUnauthorized,
    http.Client? httpClient,
    Duration timeout = const Duration(seconds: 12),
    Uuid uuid = const Uuid(),
  }) : _baseUri = baseUri,
       _deviceCode = deviceCode,
       _accessToken = accessToken,
       _onUnauthorized = onUnauthorized,
       _http = httpClient ?? http.Client(),
       _timeout = timeout,
       _uuid = uuid;

  final Uri _baseUri;
  final String? Function() _deviceCode;
  final String? Function() _accessToken;
  final void Function() _onUnauthorized;
  final http.Client _http;
  final Duration _timeout;
  final Uuid _uuid;

  Future<ApiResponse> get(
    String path, {
    Map<String, String>? queryParameters,
  }) => _send('GET', path, queryParameters: queryParameters);

  Future<ApiResponse> post(
    String path, {
    Object? body,
    String? idempotencyKey,
    bool mutation = true,
  }) => _send(
    'POST',
    path,
    body: body,
    idempotencyKey: idempotencyKey,
    mutation: mutation,
  );

  Future<ApiResponse> put(String path, {Object? body}) =>
      _send('PUT', path, body: body, mutation: true);

  Future<ApiResponse> _send(
    String method,
    String path, {
    Object? body,
    String? idempotencyKey,
    bool mutation = false,
    Map<String, String>? queryParameters,
  }) async {
    final request = http.Request(
      method,
      _resolve(path, queryParameters: queryParameters),
    );
    request.headers['Accept'] = 'application/json';
    request.headers['X-Correlation-ID'] = _uuid.v4();
    final deviceCode = _deviceCode();
    if (deviceCode != null && deviceCode.isNotEmpty) {
      request.headers['X-Device-Code'] = deviceCode;
    }
    final token = _accessToken();
    if (token != null && token.isNotEmpty) {
      request.headers['Authorization'] = 'Bearer $token';
    }
    if (idempotencyKey != null) {
      request.headers['Idempotency-Key'] = idempotencyKey;
    }
    if (body != null) {
      request.headers['Content-Type'] = 'application/json; charset=utf-8';
      request.body = jsonEncode(body);
    }

    try {
      final streamed = await _http.send(request).timeout(_timeout);
      final response = await http.Response.fromStream(streamed);
      final decoded = _decode(response.body);
      if (response.statusCode >= 200 && response.statusCode < 300) {
        return ApiResponse(statusCode: response.statusCode, body: decoded);
      }
      final problem = _problem(response.statusCode, decoded);
      if (response.statusCode == 401 && token != null) {
        _onUnauthorized();
      }
      throw problem;
    } on ApiProblem {
      rethrow;
    } on TimeoutException catch (_) {
      if (mutation) {
        throw AmbiguousMutationException(idempotencyKey: idempotencyKey ?? '');
      }
      throw const ConnectivityException();
    } on http.ClientException catch (_) {
      if (mutation) {
        throw AmbiguousMutationException(idempotencyKey: idempotencyKey ?? '');
      }
      throw const ConnectivityException();
    }
  }

  Uri _resolve(String path, {Map<String, String>? queryParameters}) {
    final basePath = _baseUri.path.endsWith('/')
        ? _baseUri.path.substring(0, _baseUri.path.length - 1)
        : _baseUri.path;
    final relative = path.startsWith('/') ? path : '/$path';
    return _baseUri.replace(
      path: '$basePath$relative',
      queryParameters: queryParameters ?? const <String, String>{},
    );
  }

  static Object? _decode(String body) {
    if (body.trim().isEmpty) {
      return null;
    }
    try {
      return jsonDecode(body);
    } on FormatException {
      return body;
    }
  }

  static ApiProblem _problem(int status, Object? body) {
    final json = body is JsonMap ? body : const <String, dynamic>{};
    return ApiProblem(
      status: status,
      code: json['code'] as String? ?? 'HTTP_ERROR',
      detail: json['detail'] as String? ?? '',
      correlationId: json['correlationId']?.toString(),
    );
  }

  void close() => _http.close();
}
