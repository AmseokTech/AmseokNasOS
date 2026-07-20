//--------------------------//
//--------为 Angular 开发服务器准备并信任 localhost HTTPS 证书---------//
//--------Prepares and trusts the localhost HTTPS certificate for Angular development--------//
//-------------------------//
import { execFileSync } from 'node:child_process';
import { createPrivateKey, X509Certificate } from 'node:crypto';
import { chmodSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const certificateDirectory = resolve(webRoot, '.certs');
const certificatePath = resolve(certificateDirectory, 'localhost.pem');
const privateKeyPath = resolve(certificateDirectory, 'localhost.key');
const skipTrust = process.argv.includes('--no-trust');

function runDotnetDevCerts(arguments_, stdio = 'pipe') {
  return execFileSync('dotnet', ['dev-certs', 'https', ...arguments_], {
    cwd: webRoot,
    encoding: 'utf8',
    stdio
  });
}

function exportedCertificateIsUsable() {
  try {
    const certificate = new X509Certificate(readFileSync(certificatePath));
    createPrivateKey(readFileSync(privateKeyPath));

    const expiresAt = Date.parse(certificate.validTo);
    const renewalWindow = 7 * 24 * 60 * 60 * 1000;
    return certificate.subjectAltName?.includes('DNS:localhost') === true
      && expiresAt > Date.now() + renewalWindow;
  } catch {
    return false;
  }
}

try {
  if (!skipTrust) {
    try {
      runDotnetDevCerts(['--check', '--trust']);
    } catch {
      console.log('首次运行需要信任 localhost 开发证书，请按系统提示确认。');
      runDotnetDevCerts(['--trust'], 'inherit');
    }
  }

  mkdirSync(certificateDirectory, { recursive: true, mode: 0o700 });

  if (!exportedCertificateIsUsable()) {
    rmSync(certificatePath, { force: true });
    rmSync(privateKeyPath, { force: true });
    runDotnetDevCerts([
      '--export-path', certificatePath,
      '--format', 'Pem',
      '--no-password'
    ]);
  }

  chmodSync(certificateDirectory, 0o700);
  chmodSync(certificatePath, 0o600);
  chmodSync(privateKeyPath, 0o600);

  console.log(`本地 HTTPS 证书已就绪：${certificatePath}`);
} catch (error) {
  const detail = error instanceof Error ? error.message : String(error);
  console.error('无法准备本地 HTTPS 证书。请确认已安装 .NET SDK，并手动运行：');
  console.error('  dotnet dev-certs https --trust');
  console.error(detail);
  process.exitCode = 1;
}
