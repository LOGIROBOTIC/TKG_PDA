import 'package:flutter/material.dart';

import 'app_controller.dart';
import 'router.dart';
import 'theme.dart';

class TkgPdaApp extends StatefulWidget {
  const TkgPdaApp({required this.controller, super.key});

  final AppController controller;

  @override
  State<TkgPdaApp> createState() => _TkgPdaAppState();
}

class _TkgPdaAppState extends State<TkgPdaApp> with WidgetsBindingObserver {
  late final _router = buildRouter(widget.controller);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    switch (state) {
      case AppLifecycleState.resumed:
        widget.controller.onResumed();
      case AppLifecycleState.inactive:
      case AppLifecycleState.hidden:
      case AppLifecycleState.paused:
      case AppLifecycleState.detached:
        widget.controller.onPaused();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _router.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
    title: 'TKG PDA',
    debugShowCheckedModeBanner: false,
    theme: buildPdaTheme(),
    routerConfig: _router,
  );
}
