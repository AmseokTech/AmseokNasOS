//--------------------------//
//--------为 Angular 开发服务器生成局域网 HTTPS 证书---------//
//--------Generates a LAN HTTPS certificate for the Angular development server--------//
//-------------------------//
import { execFileSync } from 'node:child_process';
import { createPrivateKey, createPublicKey, randomBytes, X509Certificate } from 'node:crypto';
import { chmodSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { isIP } from 'node:net';
import { hostname, networkInterfaces } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const certificateDirectory = resolve(webRoot, '.certs');
const caCertificatePath = resolve(certificateDirectory, 'amseok-dev-ca.crt');
const caPrivateKeyPath = resolve(certificateDirectory, 'amseok-dev-ca.key');
const certificatePath = resolve(certificateDirectory, 'lan.pem');
const privateKeyPath = resolve(certificateDirectory, 'lan.key');
const requestPath = resolve(certificateDirectory, 'lan.csr');
const extensionsPath = resolve(certificateDirectory, 'lan.ext');

function isPrivateIpv4(address) {
  const octets = address.split('.').map(Number);
  return octets[0] === 10
    || (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31)
    || (octets[0] === 192 && octets[1] === 168);
}

function discoverCertificateHosts() {
  const addresses = Object.values(networkInterfaces())
    .flatMap(entries => entries ?? [])
    .filter(entry => entry.family === 'IPv4' && !entry.internal && isPrivateIpv4(entry.address))
    .map(entry => entry.address);

  const machineName = hostname().trim().toLowerCase();
  const configuredHosts = (process.env['AMSEOK_DEV_HOSTS'] ?? '')
    .split(',')
    .map(value => value.trim().toLowerCase())
    .filter(Boolean);

  const hosts = [...new Set([
    ...addresses,
    ...(machineName && machineName !== 'localhost' ? [machineName, `${machineName}.local`] : []),
    ...configuredHosts
  ])];

  if (hosts.length === 0) {
    throw new Error('未发现局域网地址。请通过 AMSEOK_DEV_HOSTS 指定 IP 或域名。');
  }

  return hosts;
}

function runOpenSsl(arguments_) {
  return execFileSync('openssl', arguments_, {
    cwd: webRoot,
    encoding: 'utf8',
    stdio: 'pipe'
  });
}

function certificateIsValid(path, keyPath, requiredHosts, issuer) {
  try {
    const certificate = new X509Certificate(readFileSync(path));
    const privateKey = createPrivateKey(readFileSync(keyPath));
    const certificatePublicKey = certificate.publicKey.export({ type: 'spki', format: 'der' });
    const privateKeyPublicKey = createPublicKey(privateKey).export({ type: 'spki', format: 'der' });

    const expiresAt = Date.parse(certificate.validTo);
    const renewalWindow = 7 * 24 * 60 * 60 * 1000;
    const hasEveryHost = requiredHosts.every(host => {
      const label = isIP(host) ? `IP Address:${host}` : `DNS:${host}`;
      return certificate.subjectAltName?.includes(label) === true;
    });

    return expiresAt > Date.now() + renewalWindow
      && hasEveryHost
      && certificatePublicKey.equals(privateKeyPublicKey)
      && (!issuer || certificate.checkIssued(issuer) && certificate.verify(issuer.publicKey));
  } catch {
    return false;
  }
}

function ensureCertificateAuthority() {
  if (certificateIsValid(caCertificatePath, caPrivateKeyPath, [], undefined)) {
    return new X509Certificate(readFileSync(caCertificatePath));
  }

  rmSync(caCertificatePath, { force: true });
  rmSync(caPrivateKeyPath, { force: true });
  runOpenSsl([
    'req', '-x509', '-new', '-nodes', '-newkey', 'rsa:3072', '-sha256',
    '-days', '3650',
    '-keyout', caPrivateKeyPath,
    '-out', caCertificatePath,
    '-subj', '/CN=Amseok NAS Development CA/O=Amseok NAS Development'
  ]);

  return new X509Certificate(readFileSync(caCertificatePath));
}

function writeCertificateExtensions(hosts) {
  const alternativeNames = hosts.map((host, index) => {
    const type = isIP(host) ? 'IP' : 'DNS';
    return `${type}.${index + 1} = ${host}`;
  });

  writeFileSync(extensionsPath, [
    '[v3_req]',
    'basicConstraints = critical, CA:FALSE',
    'keyUsage = critical, digitalSignature, keyEncipherment',
    'extendedKeyUsage = serverAuth',
    'subjectAltName = @alt_names',
    '',
    '[alt_names]',
    ...alternativeNames,
    ''
  ].join('\n'), { mode: 0o600 });
}

function createServerCertificate(hosts) {
  rmSync(certificatePath, { force: true });
  rmSync(privateKeyPath, { force: true });
  rmSync(requestPath, { force: true });
  rmSync(extensionsPath, { force: true });
  writeCertificateExtensions(hosts);

  const commonName = hosts[0].replaceAll('/', '_');
  runOpenSsl([
    'req', '-new', '-nodes', '-newkey', 'rsa:2048',
    '-keyout', privateKeyPath,
    '-out', requestPath,
    '-subj', `/CN=${commonName}/O=Amseok NAS Development`
  ]);
  runOpenSsl([
    'x509', '-req',
    '-in', requestPath,
    '-CA', caCertificatePath,
    '-CAkey', caPrivateKeyPath,
    '-set_serial', `0x${randomBytes(16).toString('hex')}`,
    '-out', certificatePath,
    '-days', '397',
    '-sha256',
    '-extfile', extensionsPath,
    '-extensions', 'v3_req'
  ]);

  rmSync(requestPath, { force: true });
  rmSync(extensionsPath, { force: true });
}

try {
  mkdirSync(certificateDirectory, { recursive: true, mode: 0o700 });
  const hosts = discoverCertificateHosts();
  const issuer = ensureCertificateAuthority();

  if (!certificateIsValid(certificatePath, privateKeyPath, hosts, issuer)) {
    createServerCertificate(hosts);
  }

  chmodSync(certificateDirectory, 0o700);
  chmodSync(caPrivateKeyPath, 0o600);
  chmodSync(caCertificatePath, 0o644);
  chmodSync(privateKeyPath, 0o600);
  chmodSync(certificatePath, 0o644);

  console.log('局域网 HTTPS 证书已就绪，可通过以下地址访问：');
  for (const host of hosts) {
    console.log(`  https://${host}:6521/`);
  }
  console.log(`局域网客户端需安装并信任开发 CA：${caCertificatePath}`);
} catch (error) {
  const detail = error instanceof Error ? error.message : String(error);
  console.error('无法生成局域网 HTTPS 证书。请确认已安装 OpenSSL。');
  console.error(detail);
  process.exitCode = 1;
} finally {
  rmSync(requestPath, { force: true });
  rmSync(extensionsPath, { force: true });
}
