import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../core/auth/session_memory.dart';
import '../core/errors/api_problem.dart';
import '../core/storage/device_code_store.dart';
import '../data/models/pda_models.dart';
import '../data/repositories/pda_repository.dart';
import '../features/transport/submission_coordinator.dart';

enum AppStage {
  starting,
  deviceSetup,
  login,
  machines,
  operation,
  warehousePreparation,
}

/// Roles allowed to see and use the warehouse preparation console — the same
/// set the backend's WarehousePreparation authorization policy grants.
const _warehousePreparationRoles = {'ADMIN', 'SUPERVISOR', 'WAREHOUSE_OPERATOR'};

/// WP-03B: the part the operator has chosen as the effective part for
/// compatible-variant resolution — either a main part itself or one of its
/// Related/Alt parts.
class EffectivePartSelection {
  const EffectivePartSelection({
    required this.partId,
    required this.partCode,
    required this.isMain,
  });

  final String partId;
  final String partCode;
  final bool isMain;
}

/// Customer UI — Multi-Part Transport Batch: one effective part added to the
/// multi-select cart. [command] stays null until the operator has walked
/// this item through the existing part-first -> compatible-variant ->
/// operation/slot flow and confirmed an action for it.
class BatchCartItem {
  BatchCartItem({
    required this.cartItemId,
    required this.mainPart,
    required this.effectivePart,
  });

  final String cartItemId;
  final MachineMainPartOption mainPart;
  final EffectivePartSelection effectivePart;
  TransportCommand? command;

  /// Display-only snapshot captured at confirm time (the widget has the
  /// resolved [LayoutSlot]/variant right there); avoided re-deriving these
  /// from [AppController.context] later since a different cart item may by
  /// then have switched the machine to a different active variant/layout.
  String? slotDisplayName;
  String? slotCode;
  String? actionCode;
  String? variantCode;

  bool get isConfigured => command != null;
}

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

  // WP-03B: part-first selection state (machine-level, independent of the
  // currently active variant until a compatible variant is resolved).
  List<MachineMainPartOption> _partOptions = const [];
  bool _partOptionsLoading = false;
  String? _partOptionsError;
  MachineMainPartOption? _selectedMainPart;
  EffectivePartSelection? _selectedEffectivePart;
  List<CompatibleVariant>? _compatibleVariants;
  bool _compatibleVariantsLoading = false;
  String? _compatibleVariantsError;
  bool _partFirstResolved = false;

  // Customer UI — Multi-Part Transport Batch: cart state, independent of the
  // single-item part-first fields above (which drive whichever cart item is
  // currently being configured).
  bool _batchMode = false;
  final List<BatchCartItem> _batchCart = [];
  int _batchConfiguringIndex = -1;
  final BatchSubmissionCoordinator _batchSubmission =
      BatchSubmissionCoordinator();
  final Uuid _cartIdFactory = const Uuid();

  // Customer UI — Warehouse Preparation Console.
  List<WarehousePreparationRequest> _warehouseRequests = const [];
  bool _warehouseLoading = false;
  String? _warehouseError;
  final Set<String> _warehouseMarkingReady = {};

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

  List<MachineMainPartOption> get partOptions => _partOptions;
  bool get partOptionsLoading => _partOptionsLoading;
  String? get partOptionsError => _partOptionsError;
  MachineMainPartOption? get selectedMainPart => _selectedMainPart;
  EffectivePartSelection? get selectedEffectivePart => _selectedEffectivePart;
  List<CompatibleVariant>? get compatibleVariants => _compatibleVariants;
  bool get compatibleVariantsLoading => _compatibleVariantsLoading;
  String? get compatibleVariantsError => _compatibleVariantsError;

  // Customer UI — Multi-Part Transport Batch.
  bool get batchMode => _batchMode;
  List<BatchCartItem> get batchCart => List.unmodifiable(_batchCart);
  bool get isConfiguringBatchItem =>
      _batchConfiguringIndex >= 0 && _batchConfiguringIndex < _batchCart.length;
  BatchCartItem? get currentBatchItem =>
      isConfiguringBatchItem ? _batchCart[_batchConfiguringIndex] : null;

  /// 1-based position of the item currently being configured, for "N/M" UI.
  int get currentBatchItemPosition => _batchConfiguringIndex + 1;
  bool get canReviewBatch =>
      _batchCart.isNotEmpty && _batchCart.every((item) => item.isConfigured);
  bool get hasAmbiguousBatchSubmission => _batchSubmission.hasAmbiguousRequest;

  // Customer UI — Warehouse Preparation Console.
  bool get canAccessWarehousePreparation =>
      (user?.roles ?? const []).any(_warehousePreparationRoles.contains);
  List<WarehousePreparationRequest> get warehousePreparationRequests =>
      _warehouseRequests;
  bool get warehousePreparationLoading => _warehouseLoading;
  String? get warehousePreparationError => _warehouseError;
  bool isMarkingReady(String requestId) =>
      _warehouseMarkingReady.contains(requestId);

  /// WP-03B: true while a machine is selected but the operator has not yet
  /// completed the part-first -> compatible-variant resolution gate.
  bool get needsPartSelection => _selectedMachine != null && !_partFirstResolved;

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

  Future<bool> login(String employeeNumber) async {
    if (_deviceCode == null) {
      _stage = AppStage.deviceSetup;
      notifyListeners();
      return false;
    }
    _setBusy(true);
    _clearFeedback();
    try {
      final session = await _repository.login(employeeNumber.trim());
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
    _resetPartFirstState();
    _stage = AppStage.operation;
    notifyListeners();
    await Future.wait([refreshContext(showBusy: true), _loadPartOptions()]);
  }

  void leaveMachine() {
    _stopPolling();
    _selectedMachine = null;
    _context = null;
    _selectedProductGroup = null;
    _selectedPart = null;
    _selectedSlot = null;
    _resetPartFirstState();
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

  // ---- WP-03B: part-first -> compatible-variant resolution ----

  Future<void> reloadPartOptions() => _loadPartOptions();

  void selectMainPartOption(MachineMainPartOption option) {
    _selectedMainPart = option;
    _selectedEffectivePart = null;
    _compatibleVariants = null;
    _compatibleVariantsError = null;
    notifyListeners();
  }

  Future<void> selectEffectivePart(EffectivePartSelection selection) async {
    _selectedEffectivePart = selection;
    _compatibleVariants = null;
    _compatibleVariantsError = null;
    notifyListeners();
    await _loadCompatibleVariants();
  }

  Future<void> retryCompatibleVariants() => _loadCompatibleVariants();

  Future<void> selectCompatibleVariant(CompatibleVariant variant) =>
      _resolveVariant(variant);

  /// Returns to the part-first gate without leaving the machine or losing
  /// the already-loaded operational context/slots.
  void resetPartSelection() {
    _selectedMainPart = null;
    _selectedEffectivePart = null;
    _compatibleVariants = null;
    _compatibleVariantsError = null;
    _partFirstResolved = false;
    notifyListeners();
  }

  // ---- Customer UI — Multi-Part Transport Batch ----

  void enterBatchSelection() {
    if (_batchMode) return;
    _batchMode = true;
    notifyListeners();
  }

  /// Leaves batch mode and discards the cart. Only ever called after a
  /// successful submission or an explicit operator cancel — never on a
  /// recoverable failure (ambiguous/API error), so a failed submit keeps the
  /// cart intact for retry.
  void exitBatchSelection() {
    _batchMode = false;
    _batchCart.clear();
    _batchConfiguringIndex = -1;
    _batchSubmission.clear();
    resetPartSelection();
    notifyListeners();
  }

  /// Adds [part] (main, RELATED or ALT) to the cart. Returns false without
  /// changing anything if that part is already selected.
  bool addPartToBatch(MachineMainPartOption mainPart, EffectivePartSelection part) {
    if (_batchCart.any((item) => item.effectivePart.partId == part.partId)) {
      return false;
    }
    _batchCart.add(
      BatchCartItem(
        cartItemId: _cartIdFactory.v4(),
        mainPart: mainPart,
        effectivePart: part,
      ),
    );
    notifyListeners();
    return true;
  }

  Future<void> removePartFromBatch(String cartItemId) async {
    final removedIndex = _batchCart.indexWhere(
      (item) => item.cartItemId == cartItemId,
    );
    if (removedIndex == -1) return;
    _batchCart.removeAt(removedIndex);
    if (_batchConfiguringIndex == removedIndex) {
      _batchConfiguringIndex = -1;
      resetPartSelection();
      await _advanceBatchConfiguration();
    } else if (_batchConfiguringIndex > removedIndex) {
      _batchConfiguringIndex--;
    }
    notifyListeners();
  }

  /// Starts (or resumes) the sequential per-item configuration wizard at the
  /// first not-yet-configured cart item.
  Future<void> beginBatchConfiguration() => _advanceBatchConfiguration();

  /// Removes the item currently being configured (used for the zero-variant
  /// "invalid item" case) and continues with whatever comes next.
  Future<void> skipInvalidBatchItem() async {
    final item = currentBatchItem;
    if (item == null) return;
    await removePartFromBatch(item.cartItemId);
  }

  /// Called instead of [submitTransport] from the action-confirm step while
  /// a batch item is being configured: stores the resolved command on that
  /// cart item and advances to the next unconfigured item (or to Review,
  /// once every item has a command).
  Future<void> confirmBatchItemCommand(
    TransportCommand command, {
    required LayoutSlot slot,
    required AvailableAction action,
  }) async {
    if (!isConfiguringBatchItem) return;
    final item = _batchCart[_batchConfiguringIndex];
    item.command = command;
    item.slotDisplayName = slot.displayName;
    item.slotCode = slot.code;
    item.actionCode = action.action;
    item.variantCode = _context?.activeVariantCode;
    _batchConfiguringIndex = -1;
    resetPartSelection();
    await _advanceBatchConfiguration();
  }

  Future<void> _advanceBatchConfiguration() async {
    final index = _batchCart.indexWhere((item) => !item.isConfigured);
    if (index == -1) {
      notifyListeners();
      return;
    }
    _batchConfiguringIndex = index;
    await _loadBatchItemForConfiguration(_batchCart[index]);
  }

  Future<void> _loadBatchItemForConfiguration(BatchCartItem item) async {
    _selectedMainPart = item.mainPart;
    _selectedProductGroup = null;
    _selectedPart = null;
    _selectedSlot = null;
    _selectedEffectivePart = null;
    _compatibleVariants = null;
    _compatibleVariantsError = null;
    _partFirstResolved = false;
    notifyListeners();
    await selectEffectivePart(item.effectivePart);
  }

  /// Submits the configured cart. Exactly one item preserves the existing
  /// single TransportRequest workflow; two or more use the batch endpoint
  /// with one batch idempotency key and one independent key per item.
  Future<void> submitBatch() async {
    if (!canReviewBatch || _busy) return;
    if (_batchCart.length == 1) {
      final result = await submitTransport(_batchCart.single.command!);
      if (result != null) {
        exitBatchSelection();
      }
      return;
    }
    final machine = _selectedMachine;
    if (machine == null || !_session.isAuthenticated) return;
    _setBusy(true);
    _clearFeedback();
    try {
      final commandsByCartItemId = {
        for (final item in _batchCart) item.cartItemId: item.command!,
      };
      final result = await _batchSubmission.submit(commandsByCartItemId, (
        batchKey,
        itemKeys,
      ) {
        final items = [
          for (final item in _batchCart)
            TransportBatchItemCommand(
              command: item.command!,
              idempotencyKey: itemKeys[item.cartItemId]!,
            ),
        ];
        return _repository.createTransportBatch(machine.id, batchKey, items);
      });
      _notice = result.idempotentReplay
          ? 'Grup talebi doğrulandı.'
          : 'Grup talebi oluşturuldu: ${result.items.length} işlem.';
      exitBatchSelection();
      await refreshContext();
    } on AmbiguousMutationException {
      _offline = true;
      _notice =
          'Grup gönderiminin sonucu belirsiz. Aynı seçim aynı anahtarla yeniden denenebilir.';
    } on ApiProblem catch (problem) {
      _problem = problem;
      if (problem.shouldRefetch) {
        await refreshContext();
      }
    } finally {
      _setBusy(false);
    }
  }

  Future<void> retryAmbiguousBatch() async {
    if (!_batchSubmission.hasAmbiguousRequest) return;
    _offline = false;
    await submitBatch();
  }

  // ---- Customer UI — Warehouse Preparation Console ----

  /// Kept separate from the line-operator part-selection flow: this stage
  /// never touches machine selection or part-first state, and vice versa.
  Future<void> enterWarehousePreparation() async {
    if (!canAccessWarehousePreparation) return;
    _stage = AppStage.warehousePreparation;
    _clearFeedback();
    notifyListeners();
    await loadWarehousePreparationRequests();
  }

  void leaveWarehousePreparation() {
    _stage = AppStage.machines;
    _warehouseRequests = const [];
    _warehouseError = null;
    _clearFeedback();
    notifyListeners();
  }

  Future<void> loadWarehousePreparationRequests() async {
    if (!_session.isAuthenticated) return;
    _warehouseLoading = true;
    _warehouseError = null;
    notifyListeners();
    try {
      _warehouseRequests = await _repository.warehousePreparationRequests();
      _offline = false;
    } on ConnectivityException {
      _offline = true;
      _warehouseError = 'Bağlantı yok — hazırlık listesi yüklenemedi.';
    } on ApiProblem catch (problem) {
      _warehouseError = problem.operatorMessage;
    } finally {
      _warehouseLoading = false;
      notifyListeners();
    }
  }

  /// Marks [requestId] READY. On success the item is removed from the local
  /// list (the source of truth remains the server; a refresh would drop it
  /// too since it is no longer WAITING_PREPARATION). On failure the item
  /// stays exactly as it was so the operator can retry.
  Future<void> markPreparationReady(String requestId) async {
    if (_warehouseMarkingReady.contains(requestId)) return;
    _warehouseMarkingReady.add(requestId);
    _clearFeedback();
    notifyListeners();
    try {
      await _repository.markTransportReady(requestId);
      _warehouseRequests = _warehouseRequests
          .where((request) => request.requestId != requestId)
          .toList(growable: false);
      _notice = 'Parça hazır olarak işaretlendi.';
    } on ConnectivityException {
      _offline = true;
      _notice = 'Bağlantı yok — hazır durumu doğrulanamadı. Yeniden deneyin.';
    } on AmbiguousMutationException {
      _offline = true;
      _notice =
          'Hazır durumunun sunucuya ulaştığı doğrulanamadı. Yeniden deneyebilirsiniz.';
    } on ApiProblem catch (problem) {
      _problem = problem;
    } finally {
      _warehouseMarkingReady.remove(requestId);
      notifyListeners();
    }
  }

  Future<void> _loadPartOptions() async {
    final machine = _selectedMachine;
    if (machine == null) return;
    _partOptionsLoading = true;
    _partOptionsError = null;
    notifyListeners();
    try {
      _partOptions = await _repository.machinePartOptions(machine.id);
    } on ConnectivityException {
      _partOptionsError = 'Bağlantı yok — parça listesi yüklenemedi.';
    } on ApiProblem catch (problem) {
      _partOptionsError = problem.operatorMessage;
    } finally {
      _partOptionsLoading = false;
      notifyListeners();
    }
  }

  Future<void> _loadCompatibleVariants() async {
    final machine = _selectedMachine;
    final part = _selectedEffectivePart;
    if (machine == null || part == null) return;
    _compatibleVariantsLoading = true;
    _compatibleVariantsError = null;
    notifyListeners();
    try {
      final variants = await _repository.compatibleVariants(
        machineId: machine.id,
        partId: part.partId,
      );
      _compatibleVariants = variants;
      if (variants.length == 1) {
        await _resolveVariant(variants.single);
      }
    } on ConnectivityException {
      _compatibleVariantsError = 'Bağlantı yok — uyumlu varyantlar yüklenemedi.';
    } on ApiProblem catch (problem) {
      _compatibleVariantsError = problem.operatorMessage;
    } finally {
      _compatibleVariantsLoading = false;
      notifyListeners();
    }
  }

  /// Reuses the existing authorized variant-selection mechanism
  /// ([switchVariant]) when the resolved variant differs from the machine's
  /// current active variant; never bypasses machine authorization or
  /// canChangeVariant policy. Does not touch the existing (separate, manual)
  /// product-group/part selection used for transport-command material
  /// metadata — that flow is untouched by WP-03B.
  Future<void> _resolveVariant(CompatibleVariant variant) async {
    final current = _context;
    if (current != null && current.activeVariantId == variant.variantId) {
      _partFirstResolved = true;
      notifyListeners();
      return;
    }
    final option = current == null
        ? null
        : _firstOrNull(
            current.variantLayouts.where(
              (candidate) => candidate.variantId == variant.variantId,
            ),
          );
    if (option == null) {
      _notice = 'Seçilen varyant için düzenli bir yerleşim bulunamadı.';
      notifyListeners();
      return;
    }
    await switchVariant(option);
    if (_context?.activeVariantId == variant.variantId) {
      _partFirstResolved = true;
    }
    notifyListeners();
  }

  void _resetPartFirstState() {
    _partOptions = const [];
    _partOptionsLoading = false;
    _partOptionsError = null;
    _selectedMainPart = null;
    _selectedEffectivePart = null;
    _compatibleVariants = null;
    _compatibleVariantsLoading = false;
    _compatibleVariantsError = null;
    _partFirstResolved = false;
    _batchMode = false;
    _batchCart.clear();
    _batchConfiguringIndex = -1;
    _batchSubmission.clear();
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
    _resetPartFirstState();
    _offline = false;
    _resumeValidationPending = false;
    _warehouseRequests = const [];
    _warehouseError = null;
    _warehouseLoading = false;
    _warehouseMarkingReady.clear();
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
