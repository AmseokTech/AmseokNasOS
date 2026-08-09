import { DOCUMENT, NgComponentOutlet } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnDestroy,
  Type,
  ViewChild,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { CdkDrag, CdkDragEnd } from '@angular/cdk/drag-drop';

import { TranslatePipe } from '../../core/i18n';
import { WindowFrameComponent } from '../../shared/components/window-frame/window-frame.component';
import {
  AppWindowState,
  WindowBounds,
  WindowResizeDirection,
  WINDOW_DATA,
  WINDOW_DISPLAY_STATE,
  WINDOW_ID
} from './window-state.model';
import { WindowManagerService } from './window-manager.service';

interface ActiveResize {
  readonly windowId: string;
  readonly pointerId: number;
  readonly direction: WindowResizeDirection;
  readonly startX: number;
  readonly startY: number;
  readonly startBounds: WindowBounds;
}

@Component({
  selector: 'app-window-host',
  imports: [CdkDrag, NgComponentOutlet, TranslatePipe, WindowFrameComponent],
  templateUrl: './window-host.component.html',
  styleUrl: './window-host.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WindowHostComponent implements AfterViewInit, OnDestroy {
  @ViewChild('workspace', { static: true })
  private workspaceElement!: ElementRef<HTMLDivElement>;

  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly loadedComponents = signal<ReadonlyMap<string, Type<unknown>>>(new Map());
  private readonly componentErrors = signal<ReadonlySet<string>>(new Set());
  private readonly windowInjectors = new Map<string, Injector>();
  private readonly loadingWindowIds = new Set<string>();
  private resizeObserver: ResizeObserver | null = null;
  private activeResize: ActiveResize | null = null;

  readonly manager = inject(WindowManagerService);
  readonly windows = this.manager.windows;
  readonly resizeHandles: readonly WindowResizeDirection[] = [
    'top',
    'right',
    'bottom',
    'left',
    'top-left',
    'top-right',
    'bottom-left',
    'bottom-right'
  ];

  constructor() {
    effect(() => this.synchronizeRuntimeResources(this.windows()));
    this.document.addEventListener('pointermove', this.handleResizeMove);
    this.document.addEventListener('pointerup', this.handleResizeEnd);
    this.document.addEventListener('pointercancel', this.handleResizeEnd);
  }

  ngAfterViewInit(): void {
    this.updateWorkspaceBounds();
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.updateWorkspaceBounds());
      this.resizeObserver.observe(this.workspaceElement.nativeElement);
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.document.removeEventListener('pointermove', this.handleResizeMove);
    this.document.removeEventListener('pointerup', this.handleResizeEnd);
    this.document.removeEventListener('pointercancel', this.handleResizeEnd);
    this.windowInjectors.clear();
    this.loadingWindowIds.clear();
  }

  componentFor(windowId: string): Type<unknown> | undefined {
    return this.loadedComponents().get(windowId);
  }

  injectorFor(windowState: AppWindowState): Injector {
    let windowInjector = this.windowInjectors.get(windowState.id);
    if (!windowInjector) {
      windowInjector = Injector.create({
        parent: this.injector,
        providers: [
          { provide: WINDOW_ID, useValue: windowState.id },
          { provide: WINDOW_DATA, useValue: windowState.data },
          {
            provide: WINDOW_DISPLAY_STATE,
            useValue: computed(
              () => this.manager.windows().find(({ id }) => id === windowState.id)?.displayState ??
                'minimized'
            )
          }
        ]
      });
      this.windowInjectors.set(windowState.id, windowInjector);
    }
    return windowInjector;
  }

  hasLoadError(windowId: string): boolean {
    return this.componentErrors().has(windowId);
  }

  boundsFor(windowState: AppWindowState) {
    return this.manager.renderBounds(windowState);
  }

  handleDragEnd(windowState: AppWindowState, event: CdkDragEnd): void {
    const bounds = this.manager.renderBounds(windowState);
    this.manager.move(
      windowState.id,
      bounds.x + event.distance.x,
      bounds.y + event.distance.y
    );
    event.source.reset();
  }

  beginResize(
    windowState: AppWindowState,
    event: PointerEvent,
    direction: WindowResizeDirection
  ): void {
    if (windowState.displayState !== 'normal') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.manager.focus(windowState.id);
    const bounds = this.manager.renderBounds(windowState);
    this.activeResize = {
      windowId: windowState.id,
      pointerId: event.pointerId,
      direction,
      startX: event.clientX,
      startY: event.clientY,
      startBounds: bounds
    };
    (event.currentTarget as HTMLElement | null)?.setPointerCapture?.(event.pointerId);
  }

  resizeWithKeyboard(windowState: AppWindowState, event: KeyboardEvent): void {
    if (windowState.displayState !== 'normal') {
      return;
    }
    const bounds = this.manager.renderBounds(windowState);
    const step = event.shiftKey ? 50 : 10;
    let width = bounds.width;
    let height = bounds.height;
    switch (event.key) {
      case 'ArrowLeft':
        width -= step;
        break;
      case 'ArrowRight':
        width += step;
        break;
      case 'ArrowUp':
        height -= step;
        break;
      case 'ArrowDown':
        height += step;
        break;
      default:
        return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.manager.focus(windowState.id);
    this.manager.resize(windowState.id, width, height);
  }

  private readonly handleResizeMove = (event: PointerEvent): void => {
    const resize = this.activeResize;
    if (!resize || resize.pointerId !== event.pointerId) {
      return;
    }
    event.preventDefault();
    this.manager.resizeFromHandle(
      resize.windowId,
      resize.startBounds,
      resize.direction,
      event.clientX - resize.startX,
      event.clientY - resize.startY,
      false
    );
  };

  private readonly handleResizeEnd = (event: PointerEvent): void => {
    const resize = this.activeResize;
    if (!resize || resize.pointerId !== event.pointerId) {
      return;
    }
    this.activeResize = null;
    this.manager.persistLayout(resize.windowId);
  };

  private synchronizeRuntimeResources(windows: readonly AppWindowState[]): void {
    const activeIds = new Set(windows.map((windowState) => windowState.id));
    for (const windowId of this.windowInjectors.keys()) {
      if (!activeIds.has(windowId)) {
        this.windowInjectors.delete(windowId);
      }
    }

    const currentComponents = this.loadedComponents();
    if ([...currentComponents.keys()].some((windowId) => !activeIds.has(windowId))) {
      this.loadedComponents.set(
        new Map([...currentComponents].filter(([windowId]) => activeIds.has(windowId)))
      );
    }
    if ([...this.componentErrors()].some((windowId) => !activeIds.has(windowId))) {
      this.componentErrors.set(
        new Set([...this.componentErrors()].filter((windowId) => activeIds.has(windowId)))
      );
    }

    for (const windowState of windows) {
      if (
        !currentComponents.has(windowState.id) &&
        !this.componentErrors().has(windowState.id) &&
        !this.loadingWindowIds.has(windowState.id)
      ) {
        this.loadComponent(windowState);
      }
    }
  }

  private loadComponent(windowState: AppWindowState): void {
    const definition = this.manager.definition(windowState.appId);
    if (!definition) {
      this.componentErrors.update((errors) => new Set(errors).add(windowState.id));
      return;
    }
    this.loadingWindowIds.add(windowState.id);
    void definition.loadComponent()
      .then((component) => {
        if (this.manager.windows().some(({ id }) => id === windowState.id)) {
          this.loadedComponents.update((components) =>
            new Map(components).set(windowState.id, component)
          );
        }
      })
      .catch(() => {
        if (this.manager.windows().some(({ id }) => id === windowState.id)) {
          this.componentErrors.update((errors) => new Set(errors).add(windowState.id));
        }
      })
      .finally(() => this.loadingWindowIds.delete(windowState.id));
  }

  private updateWorkspaceBounds(): void {
    const element = this.workspaceElement.nativeElement;
    if (element.clientWidth > 0 && element.clientHeight > 0) {
      this.manager.setWorkspaceBounds(element.clientWidth, element.clientHeight);
    }
  }
}
