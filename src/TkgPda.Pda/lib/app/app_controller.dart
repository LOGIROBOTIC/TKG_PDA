import 'dart:async';

import 'package:flutter/foundation.dart';

import '../core/auth/session_memory.dart';
import '../core/errors/api_problem.dart';
import '../core/storage/device_code_store.dart';
import '../data/models/pda_models.dart';
import '../data/repositories/pda_repository.dart';
import '../features/transport/submission_coordinator.dart';

enum AppStage { starting, deviceSetup, login, machines, operation }

class AppController extends ChangeNotifier {
  AppController({
    required PdaDataSource repository,
    required DeviceCodeStore deviceCodeStore,
    required SessionMemory session,
    SubmissionCoordinator? submissionCoordinator,
    bool enablePolling = true,
  }) : _repository = repository,
       _deviceCodeStore = deviceCodeStore,
       _session = session,
       _submission = submissionCoordinator ?? SubmissionCoordinator(),
       _enablePolling = enablePolling;

  final PdaDataSource _repository;
  final DeviceCodeStore _deviceCodeStore;
  final SessionMemory _session;
  final SubmissionCoordinator _submission;
  final bool _enablePolling;

  AppStage _stage = AppStage.starting;
  String? _deviceCode;
  List<MachineSummary> _machines = const [];
  MachineSummary? _selectedMachine;
  OperationalContext? _context;
  ProductGroup? _selectedProductGroup;
  ProductGroupPart? _selectedPart;
  LayoutSlot? _selectedSlot;
  bool _busy = false;
  bool _contextLoading = false;
  bool _offline = false;
  bool _foreground = true;
  bool _resumeValidationPending = false;
  ApiProblem? _problem;
  String? _notice;
  Timer? _pollTimer;
  TransportCommand? _ambiguousCommand;

  AppStage get stage => _stage;
  String? get deviceCode => _deviceCode;
  AuthenticatedUser? get user => _session.user;
  bool get isAuthenticated => _session.isAuthenticated;
  List<MachineSummary> get machines => _machines;
  MachineSummary? get selectedMachine => _selectedMachine;
  OperationalContext? get context => _context;
  ProductGroup? get selectedProductGroup => _selectedProductGroup;
  ProductGroupPart? get selectedPart => _selectedPart;
  LayoutSlot? get selectedSlot => _selectedSlot;
  bool get busy => _busy;
  bool get offline => _offline;
  ApiProblem? get problem => _problem;
  String? get notice => _notice;
  bool get hasAmbiguousSubmission => _submission.hasAmbiguousRequest;
  bool get mutationEnabled =>
      !_offline &&
      !_busy &&
      !_resumeValidationPending &&
      _session.isAuthenticated;
  bool get pollingActive => _pollTimer?.isActive ?? false;
  bool get resumeValidationPending => _resumeValidationPending;

  Future<void> initialize() async {
    _deviceCode = await _deviceCodeStore.read();
    _stage = _deviceCode == null ? AppStage.deviceSetup : AppStage.login;
    notifyListeners();
  }

  Future<void> saveDeviceCode(String value) async {
    final normalized = value.trim();
    if (normalized.isEmpty || normalized.length > 120) {
      throw const FormatException('Geçerli bir cihaz kodu girin.');
    }
    _session.clear();
    _clearOperationalState();
    _submission.clear();
    _ambiguousCommand = null;
    await _deviceCodeStore.write(normalized);
    _deviceCode = normalized;
    _stage = AppStage.login;
    _clearFeedback();
    notifyListeners();
  }

  Future<void> beginDeviceCodeChange() async {
    _session.clear();
    _clearOperationalState();
    _submission.clear();
    _ambiguousCommand = null;
    _stage = AppStage.deviceSetup;
    _clearFeedback();
    notifyListeners();
  }

  Future<bool> login(String employeeNumber, String password) async {
    if (_deviceCode == null) {
      _stage = AppStage.deviceSetup;
      notifyListeners();
      return false;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      final session = await _repository.login(employeeNumber.trim(), password);
      _session.establish(session);
      await _loadMachines();
      _stage = AppStage.machines;
      _offline = false;
      return true;
    } on ApiProblem catch (problem) {
      _problem = problem;
      return false;
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — giriş yapılamadı.';
      return false;
    } finally {
      _setBusy(false);
    }
  }

  Future<void> logout() async {
    _session.clear();
    _clearOperationalState();
    _submission.clear();
    _ambiguousCommand = null;
    _stage = _deviceCode == null ? AppStage.deviceSetup : AppStage.login;
    _clearFeedback();
    notifyListeners();
  }

  void handleUnauthorized() {
    _session.clear();
    _clearOperationalState();
    _submission.clear();
    _ambiguousCommand = null;
    _problem = const ApiProblem(
      status: 401,
      code: 'SESSION_EXPIRED',
      detail: 'Oturum sona erdi.',
      correlationId: null,
    );
    _stage = _deviceCode == null ? AppStage.deviceSetup : AppStage.login;
    notifyListeners();
  }

  Future<void> reloadMachines() async {
    if (!_session.isAuthenticated) {
      return;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      await _loadMachines();
      _offline = false;
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — makine listesi güncellenemedi.';
    } on ApiProblem catch (problem) {
      _problem = problem;
    } finally {
      _setBusy(false);
    }
  }

  Future<void> selectMachine(MachineSummary machine) async {
    _selectedMachine = machine;
    _context = null;
    _selectedProductGroup = null;
    _selectedPart = null;
    _selectedSlot = null;
    _stage = AppStage.operation;
    notifyListeners();
    await refreshContext(showBusy: true);
  }

  void leaveMachine() {
    _stopPolling();
    _selectedMachine = null;
    _context = null;
    _selectedProductGroup = null;
    _selectedPart = null;
    _selectedSlot = null;
    _stage = AppStage.machines;
    _clearFeedback();
    notifyListeners();
  }

  Future<void> refreshContext({bool showBusy = false}) async {
    final machine = _selectedMachine;
    if (machine == null || !_session.isAuthenticated || _contextLoading) {
      return;
    }
    _contextLoading = true;
    if (showBusy) {
      _setBusy(true);
    }
    try {
      final next = await _repository.operationalContext(machine.id);
      _context = next;
      _offline = false;
      _problem = null;
      _reconcileSelections(next);
      if (_submission.hasAmbiguousRequest) {
        _submission.resolveByRefresh();
        _ambiguousCommand = null;
        _notice = next.activeTransportRequests.isEmpty
            ? 'Belirsiz isteğin kabul edildiği doğrulanamadı. Yeni işlem öncesi durumu kontrol edin.'
            : 'Aktif talep sunucudan doğrulandı.';
      }
      _schedulePolling();
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — bilgiler güncel olmayabilir.';
      _stopPolling();
    } on ApiProblem catch (problem) {
      _problem = problem;
    } finally {
      _contextLoading = false;
      if (showBusy) {
        _setBusy(false);
      } else {
        notifyListeners();
      }
    }
  }

  Future<void> switchVariant(VariantLayoutOption option) async {
    final machine = _selectedMachine;
    if (machine == null || !mutationEnabled) {
      return;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      await _repository.switchVariant(
        machineId: machine.id,
        variantId: option.variantId,
        layoutId: option.layoutId,
        navigationProfileId: null,
      );
      _context = null;
      _selectedProductGroup = null;
      _selectedPart = null;
      _selectedSlot = null;
      await refreshContext();
      _notice = 'Üretim varyantı güncellendi.';
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — varyant değiştirilemedi.';
    } on AmbiguousMutationException {
      _offline = true;
      _notice = 'Varyant değişikliğinin sonucu doğrulanamadı. Yenileyin.';
    } on ApiProblem catch (problem) {
      _problem = problem;
      if (problem.shouldRefetch) {
        await refreshContext();
      }
    } finally {
      _setBusy(false);
    }
  }

  void selectProductGroup(ProductGroup? group) {
    _selectedProductGroup = group;
    _selectedPart = null;
    notifyListeners();
  }

  void selectPart(ProductGroupPart? part) {
    _selectedPart = part;
    notifyListeners();
  }

  void selectSlot(LayoutSlot? slot) {
    if (slot?.isMachineRegion ?? false) {
      return;
    }
    _selectedSlot = slot;
    notifyListeners();
  }

  TransportCommand commandFor(LayoutSlot slot, AvailableAction action) {
    final current = _context;
    if (!action.enabled || current == null) {
      throw StateError('Sunucu bu işlemi etkin göstermiyor.');
    }
    final variantId = current.activeVariantId;
    final layoutId = current.activeLayoutId;
    if (variantId == null || layoutId == null) {
      throw StateError('Aktif varyant veya layout bulunmuyor.');
    }
    final remove =
        action.action == 'REMOVE_FULL' || action.action == 'REMOVE_EMPTY';
    return TransportCommand(
      machineId: current.machine.id,
      variantId: variantId,
      layoutId: layoutId,
      navigationProfileId: current.activeNavigationProfileId,
      productGroupId: _selectedProductGroup?.id,
      partId: _selectedPart?.partId,
      containerTypeId:
          _selectedPart?.containerTypeId ?? slot.currentContainerTypeId,
      operation: action.action,
      sourceSlotId: remove ? slot.id : null,
      destinationSlotId: remove ? null : slot.id,
      priority: 100,
    );
  }

  Future<TransportResult?> submitTransport(TransportCommand command) async {
    if (!_session.isAuthenticated ||
        _busy ||
        (_offline && !_submission.hasAmbiguousRequest)) {
      return null;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      final result = await _submission.submit(
        command,
        _repository.createTransport,
      );
      _notice = result.idempotentReplay
          ? 'Mevcut talep doğrulandı.'
          : 'Taşıma talebi oluşturuldu: #${result.requestNo}';
      await refreshContext();
      _ambiguousCommand = null;
      return result;
    } on AmbiguousMutationException {
      _ambiguousCommand = command;
      _offline = true;
      _notice =
          'Gönderim sonucu belirsiz. Aynı komut aynı anahtarla yeniden denenebilir veya durum yenilenebilir.';
      return null;
    } on ApiProblem catch (problem) {
      _problem = problem;
      if (problem.shouldRefetch) {
        await refreshContext();
      }
      return null;
    } on StateError catch (error) {
      _notice = error.message;
      return null;
    } finally {
      _setBusy(false);
    }
  }

  Future<TransportResult?> retryAmbiguousTransport() async {
    final command = _ambiguousCommand;
    if (command == null || !_submission.hasAmbiguousRequest) {
      return null;
    }
    _offline = false;
    return submitTransport(command);
  }

  Future<void> cancelTransport(ActiveTransportRequest request) async {
    if (!request.canCancel || !mutationEnabled) {
      return;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      final result = await _repository.cancel(request.requestId);
      _notice = result.status == 'CANCELLED'
          ? 'Talep iptal edildi.'
          : 'İptal durumu sunucudan güncellendi.';
      await refreshContext();
    } on AmbiguousMutationException {
      _offline = true;
      _notice =
          'İptal sonucunun fiziksel olarak tamamlandığı doğrulanamadı. Durumu yenileyin.';
    } on ApiProblem catch (problem) {
      _problem = problem;
      if (problem.shouldRefetch) {
        await refreshContext();
      }
    } finally {
      _setBusy(false);
    }
  }

  Future<void> reportLoadState(LayoutSlot slot, AvailableAction action) async {
    final machine = _selectedMachine;
    if (machine == null || !action.enabled || !mutationEnabled) return;
    final target = action.action == 'MARK_FULL' ? 'FULL' : 'EMPTY';
    _setBusy(true);
    _clearFeedback();
    try {
      await _repository.updateLoadState(
        machineId: machine.id,
        slotId: slot.id,
        loadState: target,
      );
      _notice = target == 'FULL'
          ? 'Kasa dolu olarak bildirildi.'
          : 'Kasa boş olarak bildirildi.';
      await refreshContext();
    } on ConnectivityException {
      _offline = true;
      _notice = 'Yük durumu sonucunun doğrulanması için bağlantıyı yenileyin.';
    } on AmbiguousMutationException {
      _offline = true;
      _notice =
          'Yük durumu sonucu belirsiz. Yeni bir durum seçmeden önce yenileyin.';
    } on ApiProblem catch (problem) {
      _problem = problem;
      if (problem.shouldRefetch) await refreshContext();
    } finally {
      _setBusy(false);
    }
  }

  Future<void> onResumed() async {
    _foreground = true;
    if (!_session.isAuthenticated) {
      _resumeValidationPending = false;
      notifyListeners();
      return;
    }
    _resumeValidationPending = true;
    notifyListeners();
    try {
      await _repository.me();
      if (_selectedMachine != null) {
        await refreshContext(showBusy: true);
      } else {
        await reloadMachines();
      }
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — bilgiler güncel olmayabilir.';
    } on ApiProblem catch (problem) {
      _problem = problem;
    } finally {
      _resumeValidationPending = false;
      if (!_offline && _selectedMachine != null) _schedulePolling();
      notifyListeners();
    }
  }

  void onPaused() {
    _foreground = false;
    _resumeValidationPending = _session.isAuthenticated;
    _stopPolling();
    notifyListeners();
  }

  Future<void> _loadMachines() async {
    _machines = await _repository.machines();
  }

  void _reconcileSelections(OperationalContext context) {
    if (_selectedProductGroup != null) {
      _selectedProductGroup = _firstOrNull(
        context.productGroups.where(
          (group) => group.id == _selectedProductGroup!.id,
        ),
      );
    }
    if (_selectedPart != null && _selectedProductGroup != null) {
      _selectedPart = _firstOrNull(
        _selectedProductGroup!.parts.where(
          (part) => part.id == _selectedPart!.id,
        ),
      );
    }
    if (_selectedSlot != null) {
      _selectedSlot = _firstOrNull(
        context.slots.where((slot) => slot.id == _selectedSlot!.id),
      );
    }
  }

  void _schedulePolling() {
    _stopPolling();
    if (!_enablePolling ||
        !_foreground ||
        _offline ||
        _selectedMachine == null) {
      return;
    }
    final interval = (_context?.activeTransportRequests.isNotEmpty ?? false)
        ? const Duration(seconds: 4)
        : const Duration(seconds: 15);
    _pollTimer = Timer(interval, () => unawaited(refreshContext()));
  }

  void _stopPolling() {
    _pollTimer?.cancel();
    _pollTimer = null;
  }

  void _clearOperationalState() {
    _stopPolling();
    _machines = const [];
    _selectedMachine = null;
    _context = null;
    _selectedProductGroup = null;
    _selectedPart = null;
    _selectedSlot = null;
    _offline = false;
    _resumeValidationPending = false;
  }

  void _clearFeedback() {
    _problem = null;
    _notice = null;
  }

  void _setBusy(bool value) {
    _busy = value;
    notifyListeners();
  }

  @override
  void dispose() {
    _stopPolling();
    super.dispose();
  }
}

T? _firstOrNull<T>(Iterable<T> values) {
  final iterator = values.iterator;
  return iterator.moveNext() ? iterator.current : null;
}
