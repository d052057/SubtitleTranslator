import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DragDropDirective } from '../directives/drag-drop.directive';

interface ServerFile {
  name: string;
  type: 'srt' | 'vtt';
}

type TranslateResponse = { success: boolean, savedPath: string, detectedSourceLanguage: string | null };

@Component({
  imports: [CommonModule, FormsModule, DragDropDirective],
  selector: 'app-subtitle-dashboard',
  styleUrl: './subtitle-dashboard.scss',
  templateUrl: './subtitle-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubtitleDashboard {
  // Signals because these are all mutated from async HTTP callbacks, not just
  // from click handlers in this component's own template - see the comment
  // history in git blame for the OnPush bug this originally fixed.
  serverFiles = signal<ServerFile[]>([]);
  selectedServerFile = signal<ServerFile | null>(null);
  activeFile = signal<File | null>(null);
  isProcessing = signal(false);
  successMessage = signal('');

  // Plain property: [(ngModel)] updates it via a template event, which OnPush
  // already handles correctly on its own.
  targetLanguage: string = 'km';

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadServerFiles();
  }

  loadServerFiles(): void {
    this.http.get<ServerFile[]>('/api/Subtitle/files')
      .subscribe({
        next: (files) => this.serverFiles.set(files),
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
      // An uploaded file and a server-side file selection are mutually
      // exclusive sources for the same "Process" button - picking one clears
      // the other so it's always unambiguous which one gets translated.
      this.selectedServerFile.set(null);
      this.successMessage.set('');
    } else {
      alert('Please use valid .srt or .vtt files only.');
    }
  }

  selectServerFile(file: ServerFile): void {
    this.selectedServerFile.set(file);
    this.activeFile.set(null);
    this.successMessage.set('');
  }

  get hasSelection(): boolean {
    return this.activeFile() !== null || this.selectedServerFile() !== null;
  }

  submitToTranslator(): void {
    const uploadedFile = this.activeFile();
    const serverFile = this.selectedServerFile();
    if (!uploadedFile && !serverFile) return;

    this.isProcessing.set(true);
    this.successMessage.set('');

    const request$ = uploadedFile
      ? this.translateUploadedFile(uploadedFile)
      : this.translateServerFile(serverFile!);

    request$.subscribe({
      next: (response) => {
        this.isProcessing.set(false);
        this.successMessage.set(
          response.detectedSourceLanguage
            ? `File translated successfully! Detected source language: ${response.detectedSourceLanguage}`
            : 'File translated successfully!'
        );
        this.activeFile.set(null);
        this.selectedServerFile.set(null);
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

  private translateUploadedFile(file: File) {
    const payload = new FormData();
    payload.append('file', file);
    payload.append('targetLanguage', this.targetLanguage);
    return this.http.post<TranslateResponse>('/api/Subtitle/translate-and-save', payload);
  }

  private translateServerFile(file: ServerFile) {
    return this.http.post<TranslateResponse>('/api/Subtitle/translate-server-file', {
      fileName: file.name,
      targetLanguage: this.targetLanguage
    });
  }
}
