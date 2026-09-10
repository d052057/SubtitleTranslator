import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpClientModule } from '@angular/common/http';
import { DragDropDirective } from './drag-drop.directive';

interface ServerFile {
  name: string;
  type: 'srt' | 'vtt';
}

@Component({
  imports: [CommonModule, FormsModule, HttpClientModule, DragDropDirective],
  selector: 'app-subtitle-dashboard',
  styleUrl: './subtitle-dashboard.scss',
  templateUrl: './subtitle-dashboard.html',
})
export class SubtitleDashboard {
  serverFiles: ServerFile[] = [];
  selectedServerFile: ServerFile | null = null;
  activeFile: File | null = null;
  targetLanguage: string = 'es';
  isProcessing: boolean = false;
  successMessage: string = '';

  private baseApiUrl = 'https://localhost:7001/api/subtitle';

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadServerFiles();
  }

  loadServerFiles(): void {
    this.http.get<ServerFile[]>(`${this.baseApiUrl}/files`)
      .subscribe({
        next: (files) => this.serverFiles = files,
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
      this.activeFile = file;
      this.successMessage = '';
    } else {
      alert('Please use valid .srt or .vtt files only.');
    }
  }

  selectServerFile(file: ServerFile): void {
    this.selectedServerFile = file;
    this.successMessage = '';
    alert(`To translate "${file.name}", drag and drop it from your D:/medias/closecaption folder into the dashed area.`);
  }

  submitToTranslator(): void {
    if (!this.activeFile) return;

    this.isProcessing = true;
    this.successMessage = '';
    
    const payload = new FormData();
    payload.append('file', this.activeFile);
    payload.append('targetLanguage', this.targetLanguage);

    this.http.post<{success: boolean, savedPath: string}>(`${this.baseApiUrl}/translate-and-save`, payload)
      .subscribe({
        next: (response) => {
          this.isProcessing = false;
          this.successMessage = `File translated successfully!`;
          this.activeFile = null;
          this.loadServerFiles();
        },
        error: (err) => {
          console.error('Translation pipeline error:', err);
          alert('Error communicating with translation server backend.');
          this.isProcessing = false;
        }
      });
  }
}
