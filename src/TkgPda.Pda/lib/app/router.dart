import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../features/device_setup/device_setup_screen.dart';
import '../features/login/login_screen.dart';
import '../features/machines/machine_list_screen.dart';
import '../features/operation/operation_screen.dart';
import 'app_controller.dart';

GoRouter buildRouter(AppController controller) => GoRouter(
  initialLocation: '/starting',
  refreshListenable: controller,
  redirect: (context, state) {
    final target = switch (controller.stage) {
      AppStage.starting => '/starting',
      AppStage.deviceSetup => '/device-setup',
      AppStage.login => '/login',
      AppStage.machines => '/machines',
      AppStage.operation =>
        controller.selectedMachine == null
            ? '/machines'
            : '/machines/${controller.selectedMachine!.id}',
    };
    return state.matchedLocation == target ? null : target;
  },
  routes: [
    GoRoute(
      path: '/starting',
      builder: (context, state) =>
          const Scaffold(body: Center(child: CircularProgressIndicator())),
    ),
    GoRoute(
      path: '/device-setup',
      builder: (context, state) => DeviceSetupScreen(controller: controller),
    ),
    GoRoute(
      path: '/login',
      builder: (context, state) => LoginScreen(controller: controller),
    ),
    GoRoute(
      path: '/machines',
      builder: (context, state) => MachineListScreen(controller: controller),
    ),
    GoRoute(
      path: '/machines/:machineId',
      builder: (context, state) => OperationScreen(controller: controller),
    ),
  ],
);
