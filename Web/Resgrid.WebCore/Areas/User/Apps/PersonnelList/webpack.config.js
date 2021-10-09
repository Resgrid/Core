const webpack = require('webpack');

module.exports = {
    entry: ['babel-polyfill', './PersonnelList/personnelList.jsx'],
    module: {
        rules: [
            {
                test: /\.(js|jsx)$/,
                exclude: /(node_modules|bower_components)/,
                use: {
                    loader: "babel-loader"
                }
            },
            {
                test: /\.css$/i,
                use: ["style-loader", "css-loader"],
            },
            {
                test: /\.(png|jpe?g|gif)$/i,
                use: [
                    {
                        loader: 'file-loader',
                    },
                ],
            },
        ]
    },
    resolve: {
        extensions: ['.js', '.jsx']
    },
    output: {
        path: __dirname + '../../../../../wwwroot/js/react',
        publicPath: '/',
        filename: 'personnelList.js'
    }
};
