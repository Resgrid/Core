namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFunctionToGetPropertyNameFromSQL : DbMigration
    {
        public override void Up()
        {
					Sql(@"	CREATE FUNCTION [dbo].[GetProperty] (
									@UserId UNIQUEIDENTIFIER,
									@PropertyName VARCHAR(50)
									)
									RETURNS VARCHAR(500)
									AS
									BEGIN

									DECLARE @RetVal VARCHAR(500)
									DECLARE @PropPos INT, @Names VARCHAR(500), @Values VARCHAR(500), @start TINYINT, @length TINYINT

									-- Note how we prefix the first property name with a colon. Now we know that EVERY property name will be bookended with colons:
									SELECT @Names = ':' + CAST(PropertyNames AS VARCHAR(500)), @Values = PropertyValueStrings FROM Profiles WHERE UserId = @UserId

									SELECT @PropPos = PATINDEX('%:'+ @PropertyName +':S%', @Names)

									IF @PropPos = -1
									BEGIN
									SET @RetVal = NULL
									END
									ELSE
									BEGIN
									SET @PropPos = @PropPos + LEN(@PropertyName) + 4
									SELECT @Names = SUBSTRING(@Names, @PropPos, LEN(@Names))
									SELECT @start = CAST(SUBSTRING(@Names, 1, CHARINDEX(':', @Names) - 1) AS tinyint) + 1
									SELECT @length = CAST(SUBSTRING(@Names, CHARINDEX(':', @Names) + 1, ((CHARINDEX(':', @Names, CHARINDEX(':', @Names) + 1)) - (CHARINDEX(':', @Names) + 1))) AS tinyint)
									SELECT @RetVal = SUBSTRING(@Values, @start, @length)
									END

									RETURN @RetVal
									END
                ");
        }
        
        public override void Down()
        {
        }
    }
}
