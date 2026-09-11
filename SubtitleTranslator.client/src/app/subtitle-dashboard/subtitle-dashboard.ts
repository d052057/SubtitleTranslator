import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DragDropDirective } from '../directives/drag-drop.directive';

interface ServerFile {
  name: string;
  type: 'srt' | 'vtt';
}

@Component({
  imports: [CommonModule, FormsModule, DragDropDirective],
  selector: 'app-subtitle-dashboard',
  styleUrl: './subtitle-dashboard.scss',
  templateUrl: './subtitle-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubtitleDashboard {
  // These are signals, not plain fields, because they're all updated from
  // async HTTP callbacks (subscribe next/error) rather than from a click or
  // other event originating in this component's own template. Under OnPush,
  // a plain field mutated that way doesn't trigger a re-render on its own -
  // that was the bug (file list only appeared after clicking refresh, which
  // happened to also fire a template event that forced a check). Signals
  // don't have that gap: writing to one automatically notifies Angular this
  // component needs checking, from anywhere, sync or async, no manual
  // markForCheck() required.
  serverFiles = signal<ServerFile[]>([]);
  selectedServerFile = signal<ServerFile | null>(null);
  activeFile = signal<File | null>(null);
  isProcessing = signal(false);
  successMessage = signal('');

  // Plain property is fine here: [(ngModel)] updates it via (ngModelChange),
  // a template event on this component, which OnPush already picks up.
  targetLanguage: string = 'km';

  private baseApiUrl = '/api/subtitle';
  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadServerFiles();
  }

  loadServerFiles(): void {
    this.http.get<ServerFile[]>('/api/Subtitle/files')
      .subscribe({
        next: (files) => {
          console.log('Loaded server files:', files);
          this.serverFiles.set(files);
        },
        error: (err) => console.error('Failed to look up directory index:', err)
      });
  }

  onFileDroppedViaInterface(files: FileList): void {
    if (files.length === 0) return;
    this.processSelectedFile(files[0]);
  }

  onFileSelected(event: any): void {
    const files: FileList = event.target.files;
    if (files.length === 0) return;
    this.processSelectedFile(files[0]);
  }

  private processSelectedFile(file: File): void {
    const extension = file.name.split('.').pop()?.toLowerCase();
    if (extension === 'srt' || extension === 'vtt') {
      this.activeFile.set(file);
      this.successMessage.set('');
    } else {
      alert('Please use valid .srt or .vtt files only.');
    }
  }

  selectServerFile(file: ServerFile): void {
    this.selectedServerFile.set(file);
    this.successMessage.set('');
    alert(`To translate "${file.name}", drag and drop it from your D:/medias/closecaption folder into the dashed area.`);
  }

  submitToTranslator(): void {
    const file = this.activeFile();
    if (!file) return;

    this.isProcessing.set(true);
    this.successMessage.set('');

    const payload = new FormData();
    payload.append('file', file);
    payload.append('targetLanguage', this.targetLanguage);

    this.http.post<{ success: boolean, savedPath: string, detectedSourceLanguage: string | null }>('/api/Subtitle/translate-and-save', payload)
      .subscribe({
        next: (response) => {
          this.isProcessing.set(false);
          this.successMessage.set(
            response.detectedSourceLanguage
              ? `File translated successfully! Detected source language: ${response.detectedSourceLanguage}`
              : 'File translated successfully!'
          );
          this.activeFile.set(null);
          this.loadServerFiles();
        },
        error: (err) => {
          console.error('Translation pipeline error:', err);
          // The server returns a specific message for known failure cases
          // (unsupported language, bad file type, etc.) - show that instead
          // of a generic message when it's available.
          const serverMessage = typeof err?.error === 'string' ? err.error : null;
          alert(serverMessage ?? 'Error communicating with translation server backend.');
          this.isProcessing.set(false);
        }
      });
  }
}
