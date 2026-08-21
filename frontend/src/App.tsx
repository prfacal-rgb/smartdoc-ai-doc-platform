import { useAuth } from "./auth/AuthContext";
import { LoginScreen } from "./components/LoginScreen";
import { DocumentsPanel } from "./components/DocumentsPanel";
import { UploadBox } from "./components/UploadBox";
import { ChatPanel } from "./components/ChatPanel";
import { useDocuments } from "./hooks/useDocuments";
import { useChat } from "./hooks/useChat";

function Dashboard() {
  const { token, email, logout } = useAuth();
  const { documents, isLoading, error, isUploading, upload, remove } = useDocuments(token, logout);
  const { messages, isAsking, error: chatError, ask } = useChat(token, logout);

  return (
    <div className="flex min-h-screen flex-col bg-slate-100">
      <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
        <h1 className="flex items-center gap-2 text-lg font-semibold text-slate-900">
          <span aria-hidden="true">📄</span> SmartDoc
        </h1>
        <div className="flex items-center gap-3">
          {email && <span className="text-sm text-slate-500">{email}</span>}
          <button
            type="button"
            onClick={logout}
            className="rounded-md px-2 py-1 text-sm text-slate-500 transition hover:bg-slate-100 hover:text-slate-800"
          >
            Sign out
          </button>
        </div>
      </header>

      <main className="grid flex-1 grid-cols-1 gap-4 p-4 md:grid-cols-[minmax(260px,320px)_1fr] md:p-6">
        <div className="flex flex-col gap-4">
          <DocumentsPanel documents={documents} isLoading={isLoading} error={error} onDelete={remove} />
          <UploadBox isUploading={isUploading} onUpload={upload} />
        </div>

        <div className="min-h-[28rem] md:min-h-0">
          <ChatPanel messages={messages} isAsking={isAsking} error={chatError} onAsk={ask} />
        </div>
      </main>
    </div>
  );
}

export function App() {
  const { token } = useAuth();
  return token ? <Dashboard /> : <LoginScreen />;
}
