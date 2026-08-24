import '../../data/models/pda_models.dart';

class SessionMemory {
  String? _accessToken;
  DateTime? _expiresAt;
  AuthenticatedUser? _user;
  PdaDevice? _device;

  String? get accessToken => _accessToken;
  DateTime? get expiresAt => _expiresAt;
  AuthenticatedUser? get user => _user;
  PdaDevice? get device => _device;
  bool get isAuthenticated =>
      _accessToken != null &&
      _user != null &&
      (_expiresAt?.isAfter(DateTime.now().toUtc()) ?? false);

  void establish(LoginSession session) {
    _accessToken = session.accessToken;
    _expiresAt = session.expiresAt.toUtc();
    _user = session.user;
    _device = session.device;
  }

  void clear() {
    _accessToken = null;
    _expiresAt = null;
    _user = null;
    _device = null;
  }
}
