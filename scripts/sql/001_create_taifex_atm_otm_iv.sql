IF OBJECT_ID(N'dbo.TAIFEX_AtmOTMIV', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TAIFEX_AtmOTMIV (
        [交易日期] DATETIME2(0) NOT NULL,
        [合約類型] NVARCHAR(16) NOT NULL,
        [到期月份(週別)] NVARCHAR(32) NULL,
        [契約到期日] DATETIME2(0) NULL,
        [中央價平履約價] FLOAT NULL,
        [中央Call結算價] FLOAT NULL,
        [中央Put結算價] FLOAT NULL,
        [價平合成期貨] FLOAT NULL,
        [Call價外履約價] FLOAT NULL,
        [Call價外結算價] FLOAT NULL,
        [Put價外履約價] FLOAT NULL,
        [Put價外結算價] FLOAT NULL,
        [剩餘交易日] INT NULL,
        [買權IV] FLOAT NULL,
        [賣權IV] FLOAT NULL,
        [計算口徑] NVARCHAR(300) NOT NULL,
        [資料狀態] NVARCHAR(32) NOT NULL,
        [拒算原因] NVARCHAR(100) NULL,
        [來源檔] NVARCHAR(260) NULL,
        [寫入時間] DATETIME2(0) NOT NULL CONSTRAINT [DF_TAIFEX_AtmOTMIV_寫入時間] DEFAULT (SYSDATETIME()),
        CONSTRAINT [PK_TAIFEX_AtmOTMIV] PRIMARY KEY ([交易日期], [合約類型])
    );
END;
