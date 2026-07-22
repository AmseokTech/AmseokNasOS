//--------------------------//
//--------在受管窗口中管理 xterm.js 与 WebSocket 生命周期---------//
//--------Manages the xterm.js and WebSocket lifecycle inside a managed window--------//
//-------------------------//
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  effect,
  inject,
  signal
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FitAddon } from '@xterm/addon-fit';
import { Terminal } from '@xterm/xterm';

import {
  WINDOW_DATA,
  WINDOW_DISPLAY_STATE,
  WINDOW_ID,
  WindowDisplayState
} from '../../shell/window-manager/window-state.model';
import { TerminalLauncherService } from './terminal-launcher.service';
import type { TerminalSession } from './terminal-session.service';

type TerminalState = 'connecting' | 'connected' | 'closed' | 'error';

interface TerminalControlMessage {
  type: 'exited' | 'error';
  exitCode?: number | null;
  code?: string;
  message?: string;
}

@Component({
  selector: 'app-terminal-page',
  imports: [MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './terminal-page.component.html',
  styleUrl: './terminal-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TerminalPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('terminalHost', { static: true })
  private terminalHost!: ElementRef<HTMLDivElement>;

  private readonly launcher = inject(TerminalLauncherService);
  private readonly session = inject(WINDOW_DATA) as TerminalSession;
  private readonly windowDisplayState = inject(WINDOW_DISPLAY_STATE);
  private readonly windowId = inject(WINDOW_ID);
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
  private scheduledFitFrame: number | null = null;
  private destroyed = false;
  private viewInitialized = false;
  private previousDisplayState: WindowDisplayState = this.windowDisplayState();

  readonly state = signal<TerminalState>('connecting');
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    effect(() => {
      const displayState = this.windowDisplayState();
      if (
        this.viewInitialized &&
        displayState !== 'minimized' &&
        displayState !== this.previousDisplayState
      ) {
        this.scheduleFit();
      }
      this.previousDisplayState = displayState;
    });
  }

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
    this.viewInitialized = true;
  }

  reauthenticate(): void {
    this.requestClose();
    this.launcher.reauthenticate(this.windowId);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.scheduledFitFrame !== null) {
      cancelAnimationFrame(this.scheduledFitFrame);
      this.scheduledFitFrame = null;
    }
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
    if (this.scheduledFitFrame !== null) {
      cancelAnimationFrame(this.scheduledFitFrame);
    }
    this.scheduledFitFrame = requestAnimationFrame(() => {
      this.scheduledFitFrame = null;
      if (this.destroyed) {
        return;
      }
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
