import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../shared/widgets/feedback_banners.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({required this.controller, super.key});

  final AppController controller;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _employeeNumber = TextEditingController();
  final _password = TextEditingController();
  bool _obscurePassword = true;

  @override
  void dispose() {
    _employeeNumber.dispose();
    _password.clear();
    _password.dispose();
    super.dispose();
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate() || widget.controller.busy) {
      return;
    }
    FocusScope.of(context).unfocus();
    await widget.controller.login(_employeeNumber.text, _password.text);
    _password.clear();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: widget.controller,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        title: const Text('TKG Operatör Girişi'),
        actions: [
          TextButton.icon(
            key: const Key('change-device-code'),
            onPressed: widget.controller.busy ? null : _confirmDeviceChange,
            icon: const Icon(Icons.settings, color: Colors.white),
            label: const Text(
              'Cihazı Değiştir',
              style: TextStyle(color: Colors.white),
            ),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (widget.controller.offline) const OfflineBanner(),
            if (widget.controller.problem case final problem?)
              ProblemBanner(problem: problem),
            Expanded(
              child: Center(
                child: SingleChildScrollView(
                  padding: const EdgeInsets.all(24),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 520),
                    child: Card(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Form(
                          key: _formKey,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              const Icon(Icons.factory, size: 64),
                              const SizedBox(height: 16),
                              Text(
                                'Cihaz: ${widget.controller.deviceCode}',
                                textAlign: TextAlign.center,
                                style: const TextStyle(
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                              const SizedBox(height: 24),
                              TextFormField(
                                key: const Key('employee-number-field'),
                                controller: _employeeNumber,
                                textInputAction: TextInputAction.next,
                                autocorrect: false,
                                decoration: const InputDecoration(
                                  labelText: 'Sicil Numarası',
                                  prefixIcon: Icon(Icons.badge),
                                ),
                                validator: (value) =>
                                    (value?.trim().isEmpty ?? true)
                                    ? 'Sicil numarası zorunludur.'
                                    : null,
                              ),
                              const SizedBox(height: 16),
                              TextFormField(
                                key: const Key('password-field'),
                                controller: _password,
                                obscureText: _obscurePassword,
                                enableSuggestions: false,
                                autocorrect: false,
                                decoration: InputDecoration(
                                  labelText: 'Şifre',
                                  prefixIcon: const Icon(Icons.password),
                                  suffixIcon: IconButton(
                                    onPressed: () => setState(
                                      () =>
                                          _obscurePassword = !_obscurePassword,
                                    ),
                                    icon: Icon(
                                      _obscurePassword
                                          ? Icons.visibility
                                          : Icons.visibility_off,
                                    ),
                                  ),
                                ),
                                validator: (value) => (value?.isEmpty ?? true)
                                    ? 'Şifre zorunludur.'
                                    : null,
                                onFieldSubmitted: (_) => _login(),
                              ),
                              const SizedBox(height: 22),
                              ElevatedButton.icon(
                                key: const Key('login-button'),
                                onPressed: widget.controller.busy
                                    ? null
                                    : _login,
                                icon: widget.controller.busy
                                    ? const SizedBox.square(
                                        dimension: 22,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 3,
                                        ),
                                      )
                                    : const Icon(Icons.login),
                                label: const Text('Giriş Yap'),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Future<void> _confirmDeviceChange() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Cihazı değiştir / yeniden yapılandır'),
        content: const Text(
          'Bu terminal kimliği değiştirilecek. Oturum ve ekrandaki operasyon durumu temizlenecek; yeni cihazın Admin Web üzerinde önceden tanımlı olması gerekir.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Vazgeç'),
          ),
          ElevatedButton(
            key: const Key('confirm-device-code-change'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Yeniden Yapılandır'),
          ),
        ],
      ),
    );
    if (confirmed == true) await widget.controller.beginDeviceCodeChange();
  }
}
