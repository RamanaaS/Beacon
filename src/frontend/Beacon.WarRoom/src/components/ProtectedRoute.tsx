import { observer } from "mobx-react-lite";
import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { authStore } from "../stores/AuthStore";

export const ProtectedRoute = observer(({ children }: { children: ReactNode }) => {
  if (!authStore.isAuthenticated) return <Navigate to="/login" replace />;
  return <>{children}</>;
});
