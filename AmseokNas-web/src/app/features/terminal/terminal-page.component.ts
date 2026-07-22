//--------------------------//
//--------在 Material 弹窗中管理 xterm.js 与 WebSocket 生命周期---------//
//--------Manages the xterm.js and WebSocket lifecycle inside a Material dialog--------//
//-------------------------//
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  inject,
  signal
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FitAddon } from '@xterm/addon-fit';
import { Terminal } from '@xterm/xterm';

import { WindowFrameComponent } from '../../shared/components/window-frame/window-frame.component';
import type { TerminalDialogResult } from './terminal-launcher.service';
import type { TerminalSession } from './terminal-session.service';

type TerminalState = 'connecting' | 'connected' | 'closed' | 'error';

interface TerminalControlMessage {
  type: 'exited' | 'error';
  exitCode?: number | null;
  code?: string;
  message?: string;
}

@Component({
  selector: 'app-terminal-dialog',
  imports: [MatButtonModule, MatProgressSpinnerModule, WindowFrameComponent],
  templateUrl: './terminal-page.component.html',
  styleUrl: './terminal-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TerminalDialogComponent implements AfterViewInit, OnDestroy {
  @ViewChild('terminalHost', { static: true })
  private terminalHost!: ElementRef<HTMLDivElement>;

  private readonly dialogRef = inject(
    MatDialogRef<TerminalDialogComponent, TerminalDialogResult>
  );
  private readonly session = inject<TerminalSession>(MAT_DIALOG_DATA);
  private readonly terminal = new Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: true,
    cursorStyle: 'block',
    fontFamily: '"SFMono-Regular", Consolas, "Liberation Mono", monospace',
    fontSize: 14,
    scrollback: 2_000,
    theme: {
      background: '#202020',
      foreground: '#f2f2f2',
      cursor: '#f2f2f2',
      selectionBackground: '#5a5a5a'
    }
  });
  private readonly fitAddon = new FitAddon();
  private readonly encoder = new TextEncoder();
  private socket: WebSocket | null = null;
  private resizeObserver: ResizeObserver | null = null;

  readonly state = signal<TerminalState>('connecting');
  readonly errorMessage = signal<string | null>(null);
  readonly maximized = signal(false);

  ngAfterViewInit(): void {
    this.terminal.loadAddon(this.fitAddon);
    this.terminal.open(this.terminalHost.nativeElement);
    this.fitAddon.fit();
    this.terminal.onData((data) => {
      if (this.socket?.readyState === WebSocket.OPEN) {
        this.socket.send(this.encoder.encode(data));
      }
    });
    this.resizeObserver = new ResizeObserver(() => {
      this.fitAddon.fit();
      this.sendResize();
    });
    this.resizeObserver.observe(this.terminalHost.nativeElement);
    this.openSocket(this.session.webSocketPath);
  }

  close(): void {
    this.requestClose();
    this.dialogRef.close();
  }

  minimize(): void {
    this.dialogRef.addPanelClass('window-frame-panel--minimized');
  }

  restore(): void {
    this.dialogRef.removePanelClass('window-frame-panel--minimized');
    this.scheduleFit();
  }

  toggleMaximize(): void {
    const maximized = !this.maximized();
    this.maximized.set(maximized);
    if (maximized) {
      this.dialogRef.addPanelClass('window-frame-panel--maximized');
      this.dialogRef.updateSize('100vw', '100vh');
      this.dialogRef.updatePosition({ top: '0', left: '0' });
    } else {
      this.dialogRef.removePanelClass('window-frame-panel--maximized');
      this.dialogRef.updateSize(
        'min(1120px, calc(100vw - 24px))',
        'min(760px, calc(100vh - 24px))'
      );
      this.dialogRef.updatePosition();
    }
    this.scheduleFit();
  }

  reauthenticate(): void {
    this.requestClose();
    this.dialogRef.close('reauthenticate');
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.requestClose();
    this.terminal.dispose();
  }

  private openSocket(path: string): void {
    const url = new URL(path, window.location.href);
    url.protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(url, ['amseoknas-terminal.v1']);
    socket.binaryType = 'arraybuffer';
    this.socket = socket;

    socket.addEventListener('open', () => {
      this.state.set('connected');
      this.terminal.focus();
      this.sendResize();
    });
    socket.addEventListener('message', (event: MessageEvent<ArrayBuffer | string>) => {
      if (event.data instanceof ArrayBuffer) {
        this.terminal.write(new Uint8Array(event.data));
        return;
      }

      this.handleControlMessage(event.data);
    });
    socket.addEventListener('error', () => {
      this.errorMessage.set('终端连接异常，请确认 broker 和 WebSocket 代理配置');
      this.state.set('error');
    });
    socket.addEventListener('close', (event) => {
      if (this.socket === socket) {
        this.socket = null;
      }
      if (this.state() === 'connected' || this.state() === 'connecting') {
        this.state.set(event.code === 1000 ? 'closed' : 'error');
        if (event.code !== 1000) {
          this.errorMessage.set('终端连接已意外断开');
        }
      }
    });
  }

  private handleControlMessage(payload: string): void {
    let message: TerminalControlMessage;
    try {
      message = JSON.parse(payload) as TerminalControlMessage;
    } catch {
      this.errorMessage.set('终端网关返回了无法识别的消息');
      this.state.set('error');
      this.closeSocket();
      return;
    }

    if (message.type === 'exited') {
      const suffix = message.exitCode == null ? '' : `，退出码 ${message.exitCode}`;
      this.terminal.writeln(`\r\n\x1b[38;5;214mShell 已退出${suffix}。\x1b[0m`);
      this.state.set('closed');
      this.closeSocket();
      return;
    }
    if (message.type === 'error') {
      this.errorMessage.set(message.message ?? '终端 broker 拒绝了当前会话');
      this.state.set('error');
      this.closeSocket();
    }
  }

  private sendResize(): void {
    if (this.socket?.readyState !== WebSocket.OPEN) {
      return;
    }
    this.socket.send(
      JSON.stringify({
        type: 'resize',
        columns: this.terminal.cols,
        rows: this.terminal.rows
      })
    );
  }

  private scheduleFit(): void {
    requestAnimationFrame(() => {
      this.fitAddon.fit();
      this.terminal.focus();
      this.sendResize();
    });
  }

  private requestClose(): void {
    if (this.socket?.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify({ type: 'close' }));
    }
    this.closeSocket();
  }

  private closeSocket(): void {
    const socket = this.socket;
    this.socket = null;
    if (socket && socket.readyState < WebSocket.CLOSING) {
      socket.close(1000, 'Terminal closed by user');
    }
  }
}
