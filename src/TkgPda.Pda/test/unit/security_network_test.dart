import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/config/app_config.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/network/api_client.dart';
import 'package:tkg_pda/core/platform/platform_info.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/core/storage/device_code_store_secure.dart';
import 'package:tkg_pda/data/models/pda_models.dart';
import 'package:tkg_pda/data/repositories/pda_repository.dart';

import '../support/fakes.dart';

void main() {
  group('secure local state', () {
    test('device code persists and can be cleared', () async {
      final store = MemoryDeviceCodeStore();

      await store.write('  PDA-42  ');

      expect(await store.read(), 'PDA-42');
      await store.clear();
      expect(await store.read(), isNull);
    });

    test('JWT exists only in memory and clear removes the full session', () {
      final memory = SessionMemory();
      memory.establish(
        LoginSession(
          accessToken: 'ephemeral',
          expiresAt: DateTime.now().toUtc().add(const Duration(minutes: 5)),
          user: testUser,
          device: testDevice,
        ),
      );

      expect(memory.accessToken, 'ephemeral');
      expect(memory.isAuthenticated, isTrue);
      memory.clear();
      expect(memory.accessToken, isNull);
      expect(memory.user, isNull);
      expect(memory.device, isNull);
    });

    test('password is never written to the persistent device store', () async {
      final store = MemoryDeviceCodeStore();
      await store.write(testDeviceCode);

      expect(store.value, testDeviceCode);
      expect(store.value, isNot(contains('operator-password')));
    });

    test(
      'persistent Web adapter writes only the frozen Device Code key',
      () async {
        final values = <String, String>{};
        final storage = _MapStorage(values);
        final store = PersistentDeviceCodeStore(storage);

        await store.write(' SH01A-TERM01 ');

        expect(values, {DeviceCodeStore.persistentKey: 'SH01A-TERM01'});
        expect(values.keys, isNot(contains('jwt')));
        expect(values.values, isNot(contains('operator-password')));
      },
    );

    test(
      'Android implementation retains secure-storage Device Code behavior',
      () async {
        FlutterSecureStorage.setMockInitialValues({});
        final store = SecureDeviceCodeStore();

        await store.write('PDA-ANDROID-01');

        expect(await store.read(), 'PDA-ANDROID-01');
        await store.clear();
        expect(await store.read(), isNull);
      },
    );

    test(
      'Web configuration prefers build override then runtime then same origin',
      () {
        final platform = PlatformInfo(
          isWeb: true,
          browserOrigin: Uri(scheme: 'https', host: 'line.example.test'),
        );

        expect(
          AppConfig.resolve(
            platformInfo: platform,
            runtimeApiBaseUrl: 'https://runtime.example.test',
            buildApiBaseUrl: 'https://build.example.test',
          ).apiBaseUri,
          Uri.parse('https://build.example.test'),
        );
        expect(
          AppConfig.resolve(
            platformInfo: platform,
            runtimeApiBaseUrl: 'https://runtime.example.test',
            buildApiBaseUrl: '',
          ).apiBaseUri,
          Uri.parse('https://runtime.example.test'),
        );
        expect(
          AppConfig.resolve(
            platformInfo: platform,
            runtimeApiBaseUrl: '',
            buildApiBaseUrl: '',
          ).apiBaseUri,
          Uri.parse('https://line.example.test'),
        );
      },
    );
  });

  group('HTTP security boundary', () {
    test(
      'authenticated request sends Authorization and X-Device-Code',
      () async {
        late http.Request captured;
        final client = ApiClient(
          baseUri: Uri.parse('https://api.example.test'),
          deviceCode: () => testDeviceCode,
          accessToken: () => 'memory-token',
          onUnauthorized: () {},
          httpClient: MockClient((request) async {
            captured = request;
            return http.Response('{}', 200);
          }),
        );

        await client.get('/api/v1/auth/me');

        expect(captured.headers['X-Device-Code'], testDeviceCode);
        expect(captured.headers['Authorization'], 'Bearer memory-token');
        expect(captured.headers['X-Correlation-ID'], isNotEmpty);
      },
    );

    test(
      'login request sends device code without an Authorization header',
      () async {
        late http.Request captured;
        final client = ApiClient(
          baseUri: Uri.parse('https://api.example.test'),
          deviceCode: () => testDeviceCode,
          accessToken: () => null,
          onUnauthorized: () {},
          httpClient: MockClient((request) async {
            captured = request;
            return http.Response('{}', 200);
          }),
        );

        await client.post(
          '/api/v1/auth/login',
          body: {'username': 'operator', 'password': 'transient'},
          mutation: false,
        );

        expect(captured.headers['X-Device-Code'], testDeviceCode);
        expect(captured.headers, isNot(contains('Authorization')));
      },
    );

    test('GET query parameters are encoded separately from the path', () async {
      late http.Request captured;
      final client = ApiClient(
        baseUri: Uri.parse('https://api.example.test/root'),
        deviceCode: () => testDeviceCode,
        accessToken: () => 'memory-token',
        onUnauthorized: () {},
        httpClient: MockClient((request) async {
          captured = request;
          return http.Response('{}', 200);
        }),
      );

      await client.get(
        '/api/v1/machines/m1/parts/p1/compatible-variants',
        queryParameters: {'mainPartId': 'main-1'},
      );

      expect(
        captured.url.path,
        '/root/api/v1/machines/m1/parts/p1/compatible-variants',
      );
      expect(captured.url.queryParameters['mainPartId'], 'main-1');
    });

    test(
      'PdaRepository submits employeeNumber and never a username or password field',
      () async {
        late http.Request captured;
        final client = ApiClient(
          baseUri: Uri.parse('https://api.example.test'),
          deviceCode: () => testDeviceCode,
          accessToken: () => null,
          onUnauthorized: () {},
          httpClient: MockClient((request) async {
            captured = request;
            return http.Response(
              jsonEncode({
                'accessToken': 'runtime-only-test-token',
                'tokenType': 'Bearer',
                'expiresAt': DateTime.now()
                    .toUtc()
                    .add(const Duration(minutes: 10))
                    .toIso8601String(),
                'user': {
                  'id': testUser.id,
                  'username': testUser.username,
                  'fullName': testUser.fullName,
                  'employeeNo': '001245',
                  'roles': testUser.roles,
                },
                'device': {
                  'id': testDevice.id,
                  'code': testDevice.code,
                  'name': testDevice.name,
                  'type': testDevice.type,
                },
              }),
              200,
            );
          }),
        );

        await PdaRepository(client).login('001245');

        final body = jsonDecode(captured.body) as Map<String, dynamic>;
        expect(body['employeeNumber'], '001245');
        expect(body.containsKey('username'), isFalse);
        expect(body.containsKey('password'), isFalse);
      },
    );

    test('401 invokes the session clearing callback', () async {
      var cleared = false;
      final client = ApiClient(
        baseUri: Uri.parse('https://api.example.test'),
        deviceCode: () => testDeviceCode,
        accessToken: () => 'memory-token',
        onUnauthorized: () => cleared = true,
        httpClient: MockClient(
          (_) async => http.Response(
            jsonEncode({
              'code': 'INVALID_TOKEN',
              'detail': 'expired',
              'correlationId': 'corr-401',
            }),
            401,
            headers: {'content-type': 'application/problem+json'},
          ),
        ),
      );

      await expectLater(
        client.get('/api/v1/auth/me'),
        throwsA(isA<ApiProblem>()),
      );
      expect(cleared, isTrue);
    });

    test(
      'ProblemDetails preserves stable code detail and correlation',
      () async {
        final client = ApiClient(
          baseUri: Uri.parse('https://api.example.test'),
          deviceCode: () => testDeviceCode,
          accessToken: () => null,
          onUnauthorized: () {},
          httpClient: MockClient(
            (_) async => http.Response(
              jsonEncode({
                'code': 'SLOT_ALREADY_RESERVED',
                'detail': 'slot busy',
                'correlationId': 'corr-409',
              }),
              409,
            ),
          ),
        );

        try {
          await client.get('/api/v1/test');
          fail('ApiProblem expected');
        } on ApiProblem catch (problem) {
          expect(problem.status, 409);
          expect(problem.code, 'SLOT_ALREADY_RESERVED');
          expect(problem.detail, 'slot busy');
          expect(problem.correlationId, 'corr-409');
          expect(problem.shouldRefetch, isTrue);
        }
      },
    );
  });
}

class _MapStorage implements StringKeyValueStore {
  const _MapStorage(this.values);

  final Map<String, String> values;

  @override
  String? read(String key) => values[key];

  @override
  void remove(String key) => values.remove(key);

  @override
  void write(String key, String value) => values[key] = value;
}
