import React from "react";
import { classNamesFunction, IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import { IAppProps, IAppStyleProps, IAppStyles } from "./App.types";
import { AppHeader } from "../components/AppHeader";
import { Outlet } from "react-router-dom";
import { useSelector, useDispatch } from "react-redux";
import { fetchAccount } from "../store/account.api";
import { selectAccount } from "../store/account.slice";
import { AppDispatch } from "../store";
import { useEffect } from "react";
import { useMsal } from "@azure/msal-react";


const getClassNames = classNamesFunction<IAppStyleProps, IAppStyles>();

export const AppBase: React.FunctionComponent<IAppProps> = ( props: IAppProps ) => {
  const { className, styles } = props;
  const theme = useTheme();
  const classNames: IProcessedStyleSet<IAppStyles> = getClassNames(styles, {
    className,
    theme,
  });

  const account = useSelector(selectAccount);
  const dispatch = useDispatch<AppDispatch>();
  const context = useMsal();

  useEffect(() => {
    dispatch(fetchAccount(context));
  }, []);

  if (account) {
    return (
        <div className={classNames.root}>
          <AppHeader />
          <div className={classNames.body}>
            <div className={classNames.content}>
              <Outlet />
            </div>
          </div>
        </div>
    );
  }
  else {
    return (
        <div> loading </div>
    );
  }
  
};
