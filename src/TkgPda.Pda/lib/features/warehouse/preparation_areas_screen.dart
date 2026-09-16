import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../app/design_tokens.dart';
import '../../data/models/preparation_area.dart';
import 'preparation_coordinator.dart';

/// The warehouse tablet's semi-finished preparation screen: the four shared
/// areas on the left, the machines' outstanding demands on the right.
///
/// Nothing on this screen decides anything. Each action sends the row version
/// the card is showing, so a screen left open on a pallet that has since been
/// collected and replaced is refused rather than confirming the new one.
class PreparationAreasScreen extends StatelessWidget {
  const PreparationAreasScreen({
    required this.coordinator,
    required this.onBack,
    this.title = 'Yarı Mamul Hazırlık',
    super.key,
  });

  final PreparationCoordinator coordinator;
  final VoidCallback onBack;
  final String title;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: coordinator,
    builder: (context, _) {
      final narrow =
          MediaQuery.sizeOf(context).width < PdaScale.compactBreakpoint;
      return Scaffold(
        appBar: AppBar(
          leading: IconButton(
            key: const Key('leave-preparation-areas'),
            tooltip: 'Geri',
            onPressed: coordinator.inFlight ? null : onBack,
            icon: const Icon(Icons.arrow_back),
          ),
          title: Text(title),
          actions: [
            IconButton(
              key: const Key('refresh-preparation-areas'),
              tooltip: 'Yenile',
              onPressed: coordinator.loading
                  ? null
                  : () => coordinator.refresh(),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: SafeArea(
          child: Column(
            children: [
              if (coordinator.error case final message?)
                _Banner(
                  key: const Key('preparation-error'),
                  color: PdaStatusColors.failed,
                  icon: Icons.error_outline,
                  message: message,
                ),
              if (coordinator.isNearlyFull)
                _Banner(
                  key: const Key('preparation-capacity-warning'),
                  color: PdaStatusColors.waiting,
                  icon: Icons.inventory_2_outlined,
                  message: coordinator.freeAreaCount == 0
                      ? 'Hazırlık alanlarının tamamı dolu. Bir alan boşaltılmadan yeni hazırlık yapılamaz.'
                      : 'Son bir hazırlık alanı kaldı. Lütfen boşalan alanları bildirin.',
                ),
              Expanded(
                child: narrow
                    ? _NarrowLayout(coordinator: coordinator)
                    : _WideLayout(coordinator: coordinator),
              ),
            ],
          ),
        ),
      );
    },
  );
}

class _WideLayout extends StatelessWidget {
  const _WideLayout({required this.coordinator});

  final PreparationCoordinator coordinator;

  @override
  Widget build(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Expanded(child: _AreaGrid(coordinator: coordinator, columns: 2)),
      SizedBox(width: 340, child: _DemandList(coordinator: coordinator)),
    ],
  );
}

class _NarrowLayout extends StatelessWidget {
  const _NarrowLayout({required this.coordinator});

  final PreparationCoordinator coordinator;

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Expanded(flex: 3, child: _AreaGrid(coordinator: coordinator, columns: 2)),
      const Divider(height: 1),
      Expanded(flex: 2, child: _DemandList(coordinator: coordinator)),
    ],
  );
}

class _AreaGrid extends StatelessWidget {
  const _AreaGrid({required this.coordinator, required this.columns});

  final PreparationCoordinator coordinator;
  final int columns;

  @override
  Widget build(BuildContext context) {
    if (coordinator.areas.isEmpty) {
      return Center(
        key: const Key('preparation-areas-empty'),
        child: Text(
          coordinator.loading
              ? 'Alanlar yükleniyor…'
              : 'Hazırlık alanı bulunamadı.',
          style: TextStyle(fontSize: PdaScale.body(context)),
        ),
      );
    }

    final gutter = PdaScale.gutter(context);
    return GridView.count(
      key: const Key('preparation-area-grid'),
      padding: EdgeInsets.all(gutter),
      crossAxisCount: columns,
      crossAxisSpacing: gutter,
      mainAxisSpacing: gutter,
      childAspectRatio: 1.35,
      children: [
        for (final area in coordinator.areas)
          _AreaCard(coordinator: coordinator, area: area),
      ],
    );
  }
}

class _AreaCard extends StatelessWidget {
  const _AreaCard({required this.coordinator, required this.area});

  final PreparationCoordinator coordinator;
  final PreparationArea area;

  @override
  Widget build(BuildContext context) {
    final pallet = area.pallet;
    final state = _cardState(pallet);
    final busy =
        coordinator.isBusy(area.id) ||
        (pallet != null && coordinator.isBusy(pallet.id));

    return Material(
      key: Key('preparation-area-${area.areaNumber}'),
      color: PdaStatusColors.surfaceOf(state),
      borderRadius: BorderRadius.circular(PdaScale.actionRadius),
      child: Padding(
        padding: EdgeInsets.all(PdaScale.cardPadding(context)),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    area.displayName.isNotEmpty
                        ? area.displayName
                        : 'Alan ${area.areaNumber}',
                    style: TextStyle(
                      fontSize: PdaScale.title(context),
                      fontWeight: FontWeight.w700,
                    ),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                _StatusChip(state: state),
              ],
            ),
            SizedBox(height: PdaScale.gutter(context) / 2),
            Expanded(
              child: pallet == null
                  ? Text(
                      'Bu alan boş.',
                      style: TextStyle(fontSize: PdaScale.body(context)),
                    )
                  : _PalletSummary(pallet: pallet),
            ),
            if (busy)
              const LinearProgressIndicator(minHeight: 3)
            else
              _AreaActions(coordinator: coordinator, area: area),
          ],
        ),
      ),
    );
  }

  static String _cardState(PreparationPallet? pallet) => switch (pallet) {
    null => 'VACANT',
    PreparationPallet(isReady: true) => 'READY',
    _ => 'PLACED',
  };
}

class _PalletSummary extends StatelessWidget {
  const _PalletSummary({required this.pallet});

  final PreparationPallet pallet;

  @override
  Widget build(BuildContext context) {
    final caption = TextStyle(
      fontSize: PdaScale.caption(context),
      color: Theme.of(context).colorScheme.onSurfaceVariant,
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          pallet.holdsMaterial
              ? (pallet.partCode ?? 'Parça bilinmiyor')
              : 'Boş kap',
          style: TextStyle(
            fontSize: PdaScale.body(context),
            fontWeight: FontWeight.w600,
          ),
          overflow: TextOverflow.ellipsis,
        ),
        if (pallet.containerCode.isNotEmpty)
          Text('Kap: ${pallet.containerCode}', style: caption),
        if (pallet.targetMachineCode case final machine?)
          Text('Hedef: $machine', style: caption),
        if (pallet.requestNo case final number?)
          Text('Talep #$number', style: caption),
        if (!pallet.isBoundToRequest)
          Text('Talebe bağlı değil', style: caption),
      ],
    );
  }
}

class _AreaActions extends StatelessWidget {
  const _AreaActions({required this.coordinator, required this.area});

  final PreparationCoordinator coordinator;
  final PreparationArea area;

  @override
  Widget build(BuildContext context) {
    final pallet = area.pallet;
    final height = PdaScale.secondaryActionHeight(context);

    if (!area.isEnabled) {
      return Text(
        'Bu alan devre dışı.',
        style: TextStyle(fontSize: PdaScale.caption(context)),
      );
    }

    if (pallet == null) {
      return SizedBox(
        height: height,
        width: double.infinity,
        child: FilledButton.icon(
          key: Key('fill-area-${area.areaNumber}'),
          onPressed: coordinator.inFlight
              ? null
              : () => _openDemandPicker(context),
          icon: const Icon(Icons.add),
          label: const Text('Doldur'),
        ),
      );
    }

    if (!pallet.isReady) {
      return Row(
        children: [
          Expanded(
            child: SizedBox(
              height: height,
              child: FilledButton.icon(
                key: Key('ready-area-${area.areaNumber}'),
                // Only a pallet matched to a demand can be confirmed; unmatched
                // empty stock waits until a demand is assigned to it.
                onPressed: coordinator.inFlight || !pallet.isBoundToRequest
                    ? null
                    : () => coordinator.markReady(pallet),
                icon: const Icon(Icons.check),
                label: const Text('Hazır'),
              ),
            ),
          ),
          SizedBox(width: PdaScale.gutter(context) / 2),
          SizedBox(
            height: height,
            child: OutlinedButton(
              key: Key('withdraw-area-${area.areaNumber}'),
              onPressed: coordinator.inFlight
                  ? null
                  : () => coordinator.release(pallet, 'WITHDRAWN'),
              child: const Text('Geri Al'),
            ),
          ),
        ],
      );
    }

    return Row(
      children: [
        Expanded(
          child: SizedBox(
            height: height,
            child: FilledButton.icon(
              key: Key('collected-area-${area.areaNumber}'),
              // The operator states that the robot physically took it. The
              // server never frees an area because a request changed status.
              onPressed: coordinator.inFlight
                  ? null
                  : () => coordinator.release(pallet, 'COLLECTED'),
              icon: const Icon(Icons.local_shipping_outlined),
              label: const Text('Robot Aldı'),
            ),
          ),
        ),
        SizedBox(width: PdaScale.gutter(context) / 2),
        SizedBox(
          height: height,
          child: OutlinedButton(
            key: Key('withdraw-ready-area-${area.areaNumber}'),
            onPressed: coordinator.inFlight
                ? null
                : () => coordinator.release(pallet, 'WITHDRAWN'),
            child: const Text('Geri Al'),
          ),
        ),
      ],
    );
  }

  void _openDemandPicker(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (sheetContext) => _DemandPicker(
        coordinator: coordinator,
        area: area,
        onPicked: (demand) async {
          Navigator.of(sheetContext).pop();
          await coordinator.prepareForDemand(area: area, demand: demand);
        },
      ),
    );
  }
}

class _DemandPicker extends StatelessWidget {
  const _DemandPicker({
    required this.coordinator,
    required this.area,
    required this.onPicked,
  });

  final PreparationCoordinator coordinator;
  final PreparationArea area;
  final Future<void> Function(WarehouseDemand demand) onPicked;

  @override
  Widget build(BuildContext context) {
    // A demand already being prepared somewhere else is shown but not
    // selectable: one demand, one area.
    final open = coordinator.demands
        .where((demand) => !demand.isClaimed)
        .toList(growable: false);

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.all(PdaScale.gutter(context)),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${area.displayName} için hazırlanacak talep',
              style: TextStyle(
                fontSize: PdaScale.title(context),
                fontWeight: FontWeight.w700,
              ),
            ),
            SizedBox(height: PdaScale.gutter(context)),
            if (open.isEmpty)
              Padding(
                key: const Key('demand-picker-empty'),
                padding: EdgeInsets.symmetric(
                  vertical: PdaScale.gutter(context),
                ),
                child: Text(
                  'Hazırlık bekleyen talep yok.',
                  style: TextStyle(fontSize: PdaScale.body(context)),
                ),
              )
            else
              Flexible(
                child: ListView.separated(
                  key: const Key('demand-picker-list'),
                  shrinkWrap: true,
                  itemCount: open.length,
                  separatorBuilder: (_, _) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final demand = open[index];
                    return ListTile(
                      key: Key('demand-option-${demand.requestNo}'),
                      title: Text(demand.partCode ?? 'Parça bilinmiyor'),
                      subtitle: Text(
                        '${demand.machineCode} · Talep #${demand.requestNo} · '
                        '${_waitedLabel(demand.requestedAt)}',
                      ),
                      trailing: Text(
                        demand.needsMaterial ? 'Dolu' : 'Boş',
                        style: TextStyle(
                          fontSize: PdaScale.caption(context),
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      onTap: () => onPicked(demand),
                    );
                  },
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _DemandList extends StatelessWidget {
  const _DemandList({required this.coordinator});

  final PreparationCoordinator coordinator;

  @override
  Widget build(BuildContext context) {
    final demands = coordinator.demands;
    return Container(
      color: Theme.of(context).colorScheme.surfaceContainerLow,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: EdgeInsets.all(PdaScale.gutter(context)),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    'Gelen Talepler',
                    style: TextStyle(
                      fontSize: PdaScale.title(context),
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                if (coordinator.hasUnseenDemands)
                  TextButton(
                    key: const Key('acknowledge-demands'),
                    onPressed: coordinator.acknowledgeDemands,
                    child: Text('${coordinator.unseenDemandIds.length} yeni'),
                  ),
              ],
            ),
          ),
          Expanded(
            child: demands.isEmpty
                ? Center(
                    key: const Key('demand-list-empty'),
                    child: Text(
                      'Bekleyen talep yok.',
                      style: TextStyle(fontSize: PdaScale.body(context)),
                    ),
                  )
                : ListView.separated(
                    key: const Key('demand-list'),
                    itemCount: demands.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) =>
                        _DemandTile(demand: demands[index]),
                  ),
          ),
        ],
      ),
    );
  }
}

class _DemandTile extends StatelessWidget {
  const _DemandTile({required this.demand});

  final WarehouseDemand demand;

  @override
  Widget build(BuildContext context) => ListTile(
    key: Key('demand-${demand.requestNo}'),
    dense: true,
    title: Text(
      demand.partCode ?? 'Parça bilinmiyor',
      style: TextStyle(
        fontSize: PdaScale.body(context),
        fontWeight: FontWeight.w600,
      ),
      overflow: TextOverflow.ellipsis,
    ),
    subtitle: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text('${demand.machineCode} · Talep #${demand.requestNo}'),
        Text(_waitedLabel(demand.requestedAt)),
        if (demand.claimedByAreaNumber case final number?)
          Text(
            'Alan $number hazırlıyor',
            style: TextStyle(color: PdaStatusColors.running),
          ),
      ],
    ),
  );
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({required this.state});

  final String state;

  @override
  Widget build(BuildContext context) {
    final label = switch (state) {
      'READY' => 'Hazır',
      'PLACED' => 'Hazırlanıyor',
      _ => 'Boş',
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: PdaStatusColors.of(state),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: Colors.white,
          fontSize: PdaScale.caption(context),
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({
    required this.color,
    required this.icon,
    required this.message,
    super.key,
  });

  final Color color;
  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    color: color.withValues(alpha: 0.14),
    padding: EdgeInsets.symmetric(
      horizontal: PdaScale.gutter(context),
      vertical: 10,
    ),
    child: Row(
      children: [
        Icon(icon, color: color),
        SizedBox(width: PdaScale.gutter(context) / 2),
        Expanded(
          child: Text(
            message,
            style: TextStyle(fontSize: PdaScale.body(context)),
          ),
        ),
      ],
    ),
  );
}

/// How long a demand has waited. When the server sent no timestamp the screen
/// says so rather than substituting the tablet's own clock.
String _waitedLabel(DateTime? requestedAt) {
  if (requestedAt == null) return 'İstenme zamanı bilinmiyor';
  final waited = DateTime.now().difference(requestedAt);
  final clock = DateFormat('HH:mm').format(requestedAt);
  if (waited.isNegative) return 'İstendi: $clock';
  if (waited.inMinutes < 1) return 'İstendi: $clock · az önce';
  if (waited.inMinutes < 60) return 'İstendi: $clock · ${waited.inMinutes} dk';
  return 'İstendi: $clock · ${waited.inHours} sa ${waited.inMinutes % 60} dk';
}
